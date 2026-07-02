using DynamicConfig.Library.Models;
using DynamicConfig.Library.Storage;

namespace DynamicConfig.Library.Tests.Fakes;

/// <summary>
/// Hand-rolled test double for the storage seam — no mocking library needed for a
/// one-method interface. Records what the reader asked for, serves canned data that
/// tests can mutate mid-run (simulating UI edits between polls), and can start/stop
/// failing (simulating a storage outage and recovery). Thread-safe, because the
/// Phase 3 refresh loop calls it from a background thread while tests assert.
/// </summary>
internal sealed class FakeConfigurationStorageProvider : IConfigurationStorageProvider
{
    private readonly object _gate = new();
    private readonly List<(int Threshold, TaskCompletionSource Signal)> _callWaiters = new();

    private IReadOnlyList<ConfigurationRecord> _records;
    private Exception? _exceptionToThrow;
    private int _callCount;

    public FakeConfigurationStorageProvider(params ConfigurationRecord[] records)
    {
        _records = records;
    }

    private FakeConfigurationStorageProvider(Exception exceptionToThrow)
        : this()
    {
        _exceptionToThrow = exceptionToThrow;
    }

    public static FakeConfigurationStorageProvider Throwing(Exception exception) => new(exception);

    public string? LastRequestedApplicationName { get; private set; }

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _callCount;
            }
        }
    }

    /// <summary>Replaces the served records — the next poll sees the new state.</summary>
    public void SetRecords(params ConfigurationRecord[] records)
    {
        lock (_gate)
        {
            _records = records;
        }
    }

    /// <summary>Every call from now on fails with <paramref name="exception"/> — storage is "down".</summary>
    public void StartThrowing(Exception exception)
    {
        lock (_gate)
        {
            _exceptionToThrow = exception;
        }
    }

    /// <summary>Storage "recovers": calls succeed again.</summary>
    public void StopThrowing()
    {
        lock (_gate)
        {
            _exceptionToThrow = null;
        }
    }

    /// <summary>
    /// Completes when at least <paramref name="minimumCalls"/> fetches have happened.
    /// Signal-based (TaskCompletionSource), so tests never sleep-poll the call count.
    /// </summary>
    public Task WaitForCallsAsync(int minimumCalls)
    {
        lock (_gate)
        {
            if (_callCount >= minimumCalls)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _callWaiters.Add((minimumCalls, signal));
            return signal.Task;
        }
    }

    public Task<IReadOnlyList<ConfigurationRecord>> GetActiveRecordsAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _callCount++;
            LastRequestedApplicationName = applicationName;

            for (var index = _callWaiters.Count - 1; index >= 0; index--)
            {
                if (_callCount >= _callWaiters[index].Threshold)
                {
                    _callWaiters[index].Signal.TrySetResult();
                    _callWaiters.RemoveAt(index);
                }
            }

            return _exceptionToThrow is null
                ? Task.FromResult(_records)
                : Task.FromException<IReadOnlyList<ConfigurationRecord>>(_exceptionToThrow);
        }
    }
}
