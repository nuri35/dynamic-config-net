namespace DynamicConfig.Library.Models;

/// <summary>
/// A single configuration entry, matching the record schema required by the case:
/// <c>Id, Name, Type, Value, IsActive, ApplicationName</c>.
/// Deliberately a plain POCO — storage-specific mapping lives in the storage layer
/// (see <c>MongoConfigurationClassMap</c>), keeping the domain model provider-agnostic.
/// </summary>
public sealed class ConfigurationRecord
{
    /// <summary>Unique identifier. Backed by a MongoDB ObjectId, exposed as string.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The configuration key consumers look up, e.g. <c>SiteName</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Declared value type as stored (e.g. <c>"string"</c>, <c>"Int"</c>, <c>"boolean"</c>).
    /// Parsed via <see cref="ConfigurationValueTypes.TryParse"/>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Raw value, always stored as string; converted to <see cref="Type"/> inside the library.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Only records with <c>true</c> are ever served to consumers.</summary>
    public bool IsActive { get; set; }

    /// <summary>Owning service name; each service sees only its own records.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of the last create/update. Not part of the case schema —
    /// added for cheap change detection on future refresh polls.
    /// </summary>
    public DateTime LastModifiedDate { get; set; }
}
