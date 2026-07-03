namespace DynamicConfig.Library.Messaging;

/// <summary>
/// The consumer-side broker seam (mirror of ADR 0003's storage seam): the
/// reader subscribes a callback for change signals; the transport is mockable
/// so unit tests need no live RabbitMQ. Internal — the broker integration is
/// an implementation detail behind the case-frozen public surface.
/// </summary>
internal interface IConfigurationChangeSource : IAsyncDisposable
{
    /// <summary>
    /// Connects, binds and begins delivering parsed change events to
    /// <paramref name="onConfigurationChanged"/>. Throws when the broker is
    /// unreachable — the READER decides that means polling-only mode, never a
    /// boot failure (the accelerator-not-dependency asymmetry with ADR 0004).
    /// </summary>
    Task StartAsync(
        Func<ConfigurationChangedEvent, Task> onConfigurationChanged,
        CancellationToken cancellationToken);
}
