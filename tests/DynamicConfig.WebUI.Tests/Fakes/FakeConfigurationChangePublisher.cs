using DynamicConfig.WebUI.Messaging;

namespace DynamicConfig.WebUI.Tests.Fakes;

/// <summary>
/// Hand-rolled double for the publish seam. Records every announced application
/// name and can throw on demand — the tool for pinning the fire-and-forget rule
/// (a broker failure must never fail a write).
/// </summary>
internal sealed class FakeConfigurationChangePublisher : IConfigurationChangePublisher
{
    private readonly List<string> _publishedApplicationNames = new();

    public IReadOnlyList<string> PublishedApplicationNames => _publishedApplicationNames;
    public Exception? ExceptionToThrow { get; set; }

    public Task PublishChangedAsync(string applicationName, CancellationToken cancellationToken = default)
    {
        _publishedApplicationNames.Add(applicationName);
        return ExceptionToThrow is null ? Task.CompletedTask : Task.FromException(ExceptionToThrow);
    }
}
