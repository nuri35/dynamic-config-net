using System.Globalization;
using DynamicConfig.Library.Models;

namespace DynamicConfig.Library.Conversion;

/// <summary>
/// The single source of truth for "can this raw string be read as that value type".
/// <see cref="ConfigurationValueConverter"/> uses it on the read path (throwing on
/// failure), and the WebUI's admin service uses it on the write path to reject a
/// record whose <c>Value</c> does not match its declared <c>Type</c> before it is
/// ever stored. Public so that write-side validation cannot drift from read-side
/// parsing semantics.
/// </summary>
public static class ConfigurationValueParser
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="rawValue"/> can be parsed as
    /// <paramref name="valueType"/> under the library's strict rules
    /// (invariant culture for numbers, 1/0 and true/false for booleans).
    /// </summary>
    public static bool IsParseableAs(ConfigurationValueType valueType, string? rawValue)
    {
        if (rawValue is null)
        {
            return false;
        }

        return valueType switch
        {
            ConfigurationValueType.String => true, // every non-null string is a valid string value
            ConfigurationValueType.Int => TryParseInt(rawValue, out _),
            ConfigurationValueType.Double => TryParseDouble(rawValue, out _),
            ConfigurationValueType.Bool => TryParseBool(rawValue, out _),
            _ => false,
        };
    }

    internal static bool TryParseInt(string rawValue, out int parsed) =>
        int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    // InvariantCulture is non-negotiable: stored values use '.' as the decimal separator,
    // and machine-locale parsing (e.g. tr-TR, where ',' is the separator) would corrupt
    // or reject "1.5" depending on the host's regional settings.
    internal static bool TryParseDouble(string rawValue, out double parsed) =>
        double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    // The case PDF's own sample stores booleans as 1/0, its prose uses true/false —
    // accept both spellings (case-insensitively, via bool.TryParse) like the Type parser.
    internal static bool TryParseBool(string rawValue, out bool parsed)
    {
        var normalizedValue = rawValue.Trim();
        switch (normalizedValue)
        {
            case "1":
                parsed = true;
                return true;
            case "0":
                parsed = false;
                return true;
            default:
                return bool.TryParse(normalizedValue, out parsed);
        }
    }
}
