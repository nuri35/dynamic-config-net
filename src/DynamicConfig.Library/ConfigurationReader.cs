using System.Diagnostics;
using DynamicConfig.Library.Conversion;
using DynamicConfig.Library.Exceptions;
using DynamicConfig.Library.Snapshot;
using DynamicConfig.Library.Storage;
using DynamicConfig.Library.Storage.Mongo;

namespace DynamicConfig.Library;

/// <summary>
/// The library's public face. Serves typed configuration values for exactly one
/// application from an immutable in-memory snapshot, kept fresh by a background
/// polling loop and swapped atomically (ADR 0002). Public surface is frozen by the
/// case: the 3-param constructor, <c>GetValue&lt;T&gt;</c>, and disposal plumbing.
/// </summary>
public sealed class ConfigurationReader : IDisposable, IAsyncDisposable
{
    private const int NotDisposed = 0;
    private const int Disposed = 1;

    private readonly string _applicationName;
    private readonly IConfigurationStorageProvider _storageProvider;

    // PeriodicTimer over System.Timers.Timer/System.Threading.Timer, deliberately:
    // (a) its await-based API keeps the loop a plain async method — no async void
    //     callback that would swallow exceptions and crash the process;
    // (b) ticks cannot overlap by design — WaitForNextTickAsync hands out one tick at
    //     a time, so a slow poll simply delays the next one. Overlap is eliminated
    //     structurally, not with locks. (Node analogy: setInterval callbacks can pile
    //     up behind a slow await; this is closer to a `while (true) { await sleep }`
    //     loop, which is exactly the shape written below.)
    private readonly PeriodicTimer _refreshTimer;
    private readonly CancellationTokenSource _refreshLoopCancellation = new();
    private readonly Task _refreshLoop;
    private int _disposeState = NotDisposed;

    // The single mutable cell of the whole library (ADR 0002). Written only via
    // Volatile.Write after a new snapshot is FULLY built, read via Volatile.Read —
    // the pair forms the memory barrier that guarantees a reader on another core
    // never observes a snapshot reference before the snapshot's contents.
    private ConfigurationSnapshot _snapshot;

    /// <summary>
    /// Creates a reader for <paramref name="applicationName"/>, connects it to MongoDB
    /// via <paramref name="connectionString"/>, performs the initial snapshot load and
    /// starts the background polling loop that refreshes the snapshot every
    /// <paramref name="refreshTimerIntervalInMs"/> milliseconds.
    /// </summary>
    /// <remarks>
    /// The initial load is fail-fast (ADR 0004): if storage is unreachable at
    /// construction, the storage exception propagates and no reader is created.
    /// Construct the reader at service startup so a boot-time outage surfaces to the
    /// host's restart machinery instead of as per-request errors.
    /// </remarks>
    public ConfigurationReader(string applicationName, string connectionString, int refreshTimerIntervalInMs)
        : this(applicationName, new MongoConfigurationStorageProvider(connectionString), refreshTimerIntervalInMs)
    {
    }

    /// <summary>
    /// Testability seam (see ADR 0003): the case freezes the public constructor at exactly
    /// three parameters, so dependency injection cannot enter through the public surface.
    /// This internal constructor is the DI door — unit tests (via InternalsVisibleTo)
    /// supply a mocked <see cref="IConfigurationStorageProvider"/>, no MongoDB required.
    /// </summary>
    internal ConfigurationReader(
        string applicationName,
        IConfigurationStorageProvider storageProvider,
        int refreshTimerIntervalInMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshTimerIntervalInMs);

        _applicationName = applicationName;
        _storageProvider = storageProvider;
        RefreshTimerIntervalInMs = refreshTimerIntervalInMs;

        // Fail-fast (ADR 0004): if this throws, the reader never starts its loop.
        _snapshot = LoadInitialSnapshot();

        _refreshTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(refreshTimerIntervalInMs));
        _refreshLoop = RunRefreshLoopAsync();
    }

    /// <summary>The polling period, exactly as passed to the constructor.</summary>
    internal int RefreshTimerIntervalInMs { get; }

    /// <summary>
    /// Returns the value of <paramref name="key"/> converted to <typeparamref name="T"/>.
    /// Reads only from the in-memory snapshot — no I/O, no locks on this path.
    /// Remains callable after disposal (serving the last snapshot): disposal stops
    /// refreshing, it does not invalidate immutable data a tearing-down host may still read.
    /// </summary>
    /// <exception cref="ConfigurationKeyNotFoundException">No active record with this key.</exception>
    /// <exception cref="ConfigurationTypeMismatchException">Requested type differs from the record's declared type.</exception>
    /// <exception cref="ConfigurationValueFormatException">Stored value is corrupt for its declared type.</exception>
    public T GetValue<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Copy the reference to a local ONCE and never re-read the field: everything
        // below works against one generation, so a concurrent swap cannot produce a
        // mixed-generation result within this call.
        var snapshot = Volatile.Read(ref _snapshot);

        if (!snapshot.TryGetRecord(key, out var record))
        {
            throw new ConfigurationKeyNotFoundException(key, _applicationName);
        }

        return ConfigurationValueConverter.Convert<T>(record);
    }

    /// <summary>
    /// Re-fetches the active records and atomically publishes a fresh snapshot.
    /// A failed poll (storage down, timeout, anything) is logged and changes nothing:
    /// the current snapshot IS the fallback, and recovery is automatic on the first
    /// successful poll. Internal so tests can trigger a refresh deterministically.
    /// </summary>
    internal async Task RefreshSnapshotAsync()
    {
        try
        {
            var activeRecords = await _storageProvider
                .GetActiveRecordsAsync(_applicationName, _refreshLoopCancellation.Token)
                .ConfigureAwait(false);

            var freshSnapshot = ConfigurationSnapshot.Build(activeRecords);

            // Publish only after the snapshot is completely built (see field comment).
            Volatile.Write(ref _snapshot, freshSnapshot);
        }
        catch (OperationCanceledException) when (_refreshLoopCancellation.IsCancellationRequested)
        {
            // Disposal interrupted an in-flight poll — let the loop exit; not a storage failure.
            throw;
        }
        catch (Exception storageFailure)
        {
            // No ILogger: the case-frozen constructor leaves no DI door for one, so the
            // vendor-neutral Trace channel is the observability hook hosts can attach to.
            Trace.TraceWarning(
                "DynamicConfig: refresh failed for application '{0}'; keeping the last successful snapshot. {1}",
                _applicationName,
                storageFailure);
        }
    }

    /// <summary>
    /// Stops the polling loop without blocking. The loop task finishes on its own
    /// moments later; awaiting it belongs to <see cref="DisposeAsync"/> — this sync
    /// path must not block on async work (the ctor's initial load is the project's
    /// only sanctioned sync-over-async bridge).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, Disposed) == Disposed)
        {
            return;
        }

        _refreshLoopCancellation.Cancel();
        _refreshTimer.Dispose();
        // The CancellationTokenSource is disposed in DisposeAsync, after the loop —
        // the loop may still be observing its token right now.
    }

    /// <summary>Complete teardown: stops the loop, awaits it, releases everything. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _refreshLoop.ConfigureAwait(false); // never faults: the loop swallows shutdown
        _refreshLoopCancellation.Dispose();       // multiple Dispose calls are safe by contract
    }

    private async Task RunRefreshLoopAsync()
    {
        try
        {
            // WaitForNextTickAsync returns false once the timer is disposed and throws
            // on cancellation — either disposal signal ends the loop. One tick at a
            // time: a poll slower than the interval delays the next tick, it never
            // runs concurrently with it.
            while (await _refreshTimer.WaitForNextTickAsync(_refreshLoopCancellation.Token).ConfigureAwait(false))
            {
                await RefreshSnapshotAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via Dispose().
        }
    }

    // The case freezes this synchronous constructor, so the async storage call must be
    // bridged to sync exactly once, HERE and nowhere else. Task.Run pushes the await
    // chain onto the thread pool so a SynchronizationContext host (UI, classic ASP.NET)
    // cannot deadlock; GetAwaiter().GetResult() rethrows the storage exception unwrapped
    // (unlike .Result, which wraps it in AggregateException). The hot path (GetValue)
    // and the refresh loop never block like this. Initial-load failure propagates —
    // fail-fast by decision (ADR 0004): the case's fallback clause presupposes one
    // successful load, and a config-less service would misbehave on every GetValue anyway.
    private ConfigurationSnapshot LoadInitialSnapshot()
    {
        var activeRecords = Task
            .Run(() => _storageProvider.GetActiveRecordsAsync(_applicationName))
            .GetAwaiter()
            .GetResult();

        return ConfigurationSnapshot.Build(activeRecords);
    }
}
