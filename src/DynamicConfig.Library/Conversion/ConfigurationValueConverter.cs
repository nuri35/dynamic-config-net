using System.Collections.Frozen;
using DynamicConfig.Library.Exceptions;
using DynamicConfig.Library.Models;

namespace DynamicConfig.Library.Conversion;

/// <summary>
/// Converts a record's stored string <c>Value</c> to the caller's requested type,
/// enforcing the strict declared-type contract (no widening — see
/// <see cref="ConfigurationTypeMismatchException"/>). Kept separate from the reader
/// (SRP): the reader owns lookup and lifecycle, this class owns type semantics.
/// </summary>
internal static class ConfigurationValueConverter
{
    // Frozen: this map is shared, read-only and on the hot path — same trade as the snapshot.
    private static readonly FrozenDictionary<Type, ConfigurationValueType> RequestedTypeMap =
        new Dictionary<Type, ConfigurationValueType>
        {
            [typeof(string)] = ConfigurationValueType.String,
            [typeof(int)] = ConfigurationValueType.Int,
            [typeof(double)] = ConfigurationValueType.Double,
            [typeof(bool)] = ConfigurationValueType.Bool,
        }.ToFrozenDictionary();

    internal static T Convert<T>(ConfigurationRecord record)
    {
        var isDeclaredTypeSupported = ConfigurationValueTypes.TryParse(record.Type, out var declaredType);
        var isRequestedTypeSupported = RequestedTypeMap.TryGetValue(typeof(T), out var requestedType);

        // One exception covers all three contract violations (unsupported declared type,
        // unsupported requested T, plain mismatch): the message always carries both sides,
        // which is what the caller needs to fix either the record or the call site.
        if (!isDeclaredTypeSupported || !isRequestedTypeSupported || declaredType != requestedType)
        {
            throw new ConfigurationTypeMismatchException(record.Name, record.Type, typeof(T).Name);
        }

        // The (T)(object) dance: T is proven to match declaredType above, so boxing is
        // the idiomatic way to return heterogeneous primitives from a generic method
        // without reflection. (No TypeScript-style erasure here — T is a real type.)
        return declaredType switch
        {
            ConfigurationValueType.String => (T)(object)record.Value,
            ConfigurationValueType.Int => (T)(object)ParseInt(record),
            ConfigurationValueType.Double => (T)(object)ParseDouble(record),
            ConfigurationValueType.Bool => (T)(object)ParseBool(record),
            _ => throw new ConfigurationTypeMismatchException(record.Name, record.Type, typeof(T).Name),
        };
    }

    // Parsing itself lives in ConfigurationValueParser (shared with the WebUI's
    // write-side validation); this class only adds the read-path failure semantics —
    // a corrupt stored value surfaces as ConfigurationValueFormatException.
    private static int ParseInt(ConfigurationRecord record) =>
        ConfigurationValueParser.TryParseInt(record.Value, out var parsed)
            ? parsed
            : throw new ConfigurationValueFormatException(record.Name, record.Type, record.Value);

    private static double ParseDouble(ConfigurationRecord record) =>
        ConfigurationValueParser.TryParseDouble(record.Value, out var parsed)
            ? parsed
            : throw new ConfigurationValueFormatException(record.Name, record.Type, record.Value);

    private static bool ParseBool(ConfigurationRecord record) =>
        ConfigurationValueParser.TryParseBool(record.Value, out var parsed)
            ? parsed
            : throw new ConfigurationValueFormatException(record.Name, record.Type, record.Value);
}
