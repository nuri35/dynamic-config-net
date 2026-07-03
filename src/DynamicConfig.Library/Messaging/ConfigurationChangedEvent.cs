using System.Text.Json;

namespace DynamicConfig.Library.Messaging;

/// <summary>
/// The thin config-changed signal (ADR 0005): "notify, don't transfer" — an
/// application name and a timestamp, never values. This type IS the wire
/// contract, shared by the WebUI publisher and the library consumer so the two
/// sides cannot drift (same shared-kernel reasoning as the storage constants).
/// </summary>
public sealed record ConfigurationChangedEvent(string ApplicationName, DateTime OccurredAtUtc)
{
    // camelCase on the wire, matching the WebUI API's JSON conventions.
    private static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes for publishing. Exactly two fields, pinned by test.</summary>
    public byte[] ToUtf8Json() => JsonSerializer.SerializeToUtf8Bytes(this, WireJsonOptions);

    /// <summary>
    /// Attempts to parse a received body. Returns <c>false</c> for malformed
    /// JSON or a missing/blank application name — the consumer drops such
    /// messages and keeps consuming (one bad message must never kill the flow).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8Body, out ConfigurationChangedEvent changedEvent)
    {
        changedEvent = null!;
        try
        {
            var parsed = JsonSerializer.Deserialize<ConfigurationChangedEvent>(utf8Body, WireJsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ApplicationName))
            {
                return false;
            }

            changedEvent = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
