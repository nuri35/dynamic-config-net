using System.Text;
using System.Text.Json;
using DynamicConfig.Library.Messaging;

namespace DynamicConfig.Library.Tests.Messaging;

/// <summary>
/// Pins the shared wire contract (ADR 0005 thin event) from both directions:
/// serialization emits exactly two fields, and parsing drops anything malformed
/// instead of throwing — the consumer must survive any bad message.
/// </summary>
public class ConfigurationChangedEventTests
{
    [Fact]
    public void ToUtf8Json_EmitsExactlyApplicationNameAndTimestamp_NothingElse()
    {
        var occurredAtUtc = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

        var body = new ConfigurationChangedEvent("SERVICE-A", occurredAtUtc).ToUtf8Json();

        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(body));
        var properties = json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        // Any third field (values, ids, anything) is a contract break.
        Assert.Equal(new[] { "applicationName", "occurredAtUtc" }, properties);
        Assert.Equal("SERVICE-A", json.RootElement.GetProperty("applicationName").GetString());
    }

    [Fact]
    public void TryParse_RoundTripsWhatToUtf8JsonProduced()
    {
        var original = new ConfigurationChangedEvent("SERVICE-B", DateTime.UtcNow);

        var parsed = ConfigurationChangedEvent.TryParse(original.ToUtf8Json(), out var changedEvent);

        Assert.True(parsed);
        Assert.Equal(original.ApplicationName, changedEvent.ApplicationName);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ broken")]
    [InlineData("{}")] // missing applicationName
    [InlineData("""{"applicationName":""}""")] // blank applicationName
    [InlineData("""{"applicationName":"   "}""")]
    [InlineData("[1,2,3]")] // wrong JSON shape
    public void TryParse_MalformedBody_ReturnsFalseInsteadOfThrowing(string body)
    {
        var parsed = ConfigurationChangedEvent.TryParse(Encoding.UTF8.GetBytes(body), out _);

        Assert.False(parsed); // the consumer logs and drops; one bad message never kills the flow
    }

    [Fact]
    public void TryParse_ToleratesExtraFields()
    {
        // Forward compatibility: a newer publisher adding a field must not break
        // older consumers (same IgnoreExtraElements stance as the BSON map).
        const string body = """{"applicationName":"SERVICE-A","occurredAtUtc":"2026-07-03T12:00:00Z","futureField":42}""";

        var parsed = ConfigurationChangedEvent.TryParse(Encoding.UTF8.GetBytes(body), out var changedEvent);

        Assert.True(parsed);
        Assert.Equal("SERVICE-A", changedEvent.ApplicationName);
    }
}
