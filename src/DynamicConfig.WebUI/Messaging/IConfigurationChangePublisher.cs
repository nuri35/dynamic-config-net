namespace DynamicConfig.WebUI.Messaging;

/// <summary>
/// Publishes the thin "configuration changed" signal after a successful write
/// (ADR 0005). A seam for the same reasons as ADR 0003's storage interface:
/// the service depends on this abstraction, tests mock it, and the transport
/// is swappable without touching business rules.
/// </summary>
public interface IConfigurationChangePublisher
{
    /// <summary>
    /// Announces that <paramref name="applicationName"/>'s configuration changed.
    /// Carries no values — consumers re-read storage (notify, don't transfer).
    /// May throw on broker failure; the CALLER decides that a failed signal must
    /// never fail the write (fire-and-forget discipline lives in the service).
    /// </summary>
    Task PublishChangedAsync(string applicationName, CancellationToken cancellationToken = default);
}
