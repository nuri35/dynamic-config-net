using DynamicConfig.Library.Messaging;

namespace DynamicConfig.Library.Tests.Fakes;

/// <summary>
/// Hand-rolled double for the consumer seam — same idiom as the storage fake.
/// Captures the reader's callback so tests deliver events deterministically,
/// and can fail StartAsync to simulate an unreachable broker.
/// </summary>
internal sealed class FakeConfigurationChangeSource : IConfigurationChangeSource
{
    private Func<ConfigurationChangedEvent, Task>? _onConfigurationChanged;

    public Exception? StartException { get; set; }
    public bool StartCalled { get; private set; }
    public bool Disposed { get; private set; }

    public Task StartAsync(
        Func<ConfigurationChangedEvent, Task> onConfigurationChanged,
        CancellationToken cancellationToken)
    {
        StartCalled = true;
        if (StartException is not null)
        {
            return Task.FromException(StartException);
        }

        _onConfigurationChanged = onConfigurationChanged;
        return Task.CompletedTask;
    }

    /// <summary>Delivers an event as if it arrived from the broker.</summary>
    public async Task DeliverAsync(ConfigurationChangedEvent changedEvent)
    {
        if (_onConfigurationChanged is null)
        {
            throw new InvalidOperationException("Consumer not started.");
        }

        await _onConfigurationChanged(changedEvent);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
