namespace DynamicConfig.Library.Models;

/// <summary>
/// Maps the free-text <c>Type</c> field of a stored configuration record
/// to a <see cref="ConfigurationValueType"/>.
/// </summary>
public static class ConfigurationValueTypes
{
    // The case PDF is inconsistent on purpose-like real data: the sample table uses
    // "string"/"bool"/"Int" while the prose says "integer"/"boolean". Accept all
    // spellings case-insensitively so hand-entered records don't break consumers.
    private static readonly Dictionary<string, ConfigurationValueType> StorageTypeNameMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = ConfigurationValueType.String,
            ["int"] = ConfigurationValueType.Int,
            ["integer"] = ConfigurationValueType.Int,
            ["double"] = ConfigurationValueType.Double,
            ["bool"] = ConfigurationValueType.Bool,
            ["boolean"] = ConfigurationValueType.Bool,
        };

    /// <summary>
    /// Attempts to map a storage type name (e.g. <c>"string"</c>, <c>"Int"</c>, <c>"boolean"</c>)
    /// to its <see cref="ConfigurationValueType"/>. Matching is case-insensitive and
    /// tolerates surrounding whitespace.
    /// </summary>
    /// <returns><c>true</c> when the name maps to a supported type; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? storageTypeName, out ConfigurationValueType valueType)
    {
        valueType = default;
        if (string.IsNullOrWhiteSpace(storageTypeName))
        {
            return false;
        }

        return StorageTypeNameMap.TryGetValue(storageTypeName.Trim(), out valueType);
    }
}
