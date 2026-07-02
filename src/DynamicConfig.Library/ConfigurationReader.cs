using DynamicConfig.Library.Conversion;
using DynamicConfig.Library.Exceptions;
using DynamicConfig.Library.Snapshot;
using DynamicConfig.Library.Storage;
using DynamicConfig.Library.Storage.Mongo;

namespace DynamicConfig.Library;

/// <summary>
/// The library's public face. Serves typed configuration values for exactly one
/// application from an immutable in-memory snapshot loaded from storage.
/// Public surface is frozen by the case: this constructor and <c>GetValue&lt;T&gt;</c>.
/// </summary>
public sealed class ConfigurationReader
{
    private readonly string _applicationName;
    private readonly IConfigurationStorageProvider _storageProvider;

    // Deliberately NOT readonly: Phase 3's polling loop replaces this reference
    // atomically (volatile/Interlocked, ADR 0002). The snapshot object itself is
    // immutable — only the reference ever changes.
    private ConfigurationSnapshot _snapshot;

    /// <summary>
    /// Creates a reader for <paramref name="applicationName"/>, connects it to MongoDB
    /// via <paramref name="connectionString"/> and performs the initial snapshot load.
    /// <paramref name="refreshTimerIntervalInMs"/> is validated and stored now; the
    /// polling refresh loop that consumes it lands in Phase 3.
    /// </summary>
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

        _snapshot = LoadInitialSnapshot();
    }

    /// <summary>Stored for the Phase 3 polling loop; exposed internally so tests can verify it.</summary>
    internal int RefreshTimerIntervalInMs { get; }

    /// <summary>
    /// Returns the value of <paramref name="key"/> converted to <typeparamref name="T"/>.
    /// Reads only from the in-memory snapshot — no I/O, no locks on this path.
    /// </summary>
    /// <exception cref="ConfigurationKeyNotFoundException">No active record with this key.</exception>
    /// <exception cref="ConfigurationTypeMismatchException">Requested type differs from the record's declared type.</exception>
    /// <exception cref="ConfigurationValueFormatException">Stored value is corrupt for its declared type.</exception>
    public T GetValue<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_snapshot.TryGetRecord(key, out var record))
        {
            throw new ConfigurationKeyNotFoundException(key, _applicationName);
        }

        return ConfigurationValueConverter.Convert<T>(record);
    }

    // The case freezes this synchronous constructor, so the async storage call must be
    // bridged to sync exactly once, HERE and nowhere else. Task.Run pushes the await
    // chain onto the thread pool so a SynchronizationContext host (UI, classic ASP.NET)
    // cannot deadlock; GetAwaiter().GetResult() rethrows the storage exception unwrapped
    // (unlike .Result, which wraps it in AggregateException). The hot path (GetValue)
    // and the Phase 3 refresh loop never block like this. Initial-load FAILURE currently
    // propagates to the caller — whether it should become empty-snapshot-plus-retry is
    // ADR 0004, decided in Phase 3.
    private ConfigurationSnapshot LoadInitialSnapshot()
    {
        var activeRecords = Task
            .Run(() => _storageProvider.GetActiveRecordsAsync(_applicationName))
            .GetAwaiter()
            .GetResult();

        return ConfigurationSnapshot.Build(activeRecords);
    }
}
