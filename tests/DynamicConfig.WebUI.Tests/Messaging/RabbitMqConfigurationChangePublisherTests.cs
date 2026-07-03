using System.Text;
using System.Text.Json;
using DynamicConfig.WebUI.Messaging;

namespace DynamicConfig.WebUI.Tests.Messaging;

/// <summary>
/// Pins the wire contract. Broker connectivity itself is smoke-tested against
/// the real container — mocking the AMQP client internals is low value.
/// </summary>
public class RabbitMqConfigurationChangePublisherTests
{
    [Fact]
    public void BuildEventBody_ContainsExactlyApplicationNameAndTimestamp_NothingElse()
    {
        var occurredAtUtc = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

        var body = RabbitMqConfigurationChangePublisher.BuildEventBody("SERVICE-A", occurredAtUtc);

        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(body));
        var properties = json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();

        // ADR 0005 thin event: any third field (values, ids, anything) is a contract break.
        Assert.Equal(new[] { "applicationName", "occurredAtUtc" }, properties);
        Assert.Equal("SERVICE-A", json.RootElement.GetProperty("applicationName").GetString());
        Assert.Equal(occurredAtUtc, json.RootElement.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime());
    }
}
