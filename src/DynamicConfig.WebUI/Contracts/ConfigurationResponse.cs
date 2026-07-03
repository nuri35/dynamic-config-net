using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Contracts;

/// <summary>
/// The read shape of a configuration record. The storage entity never crosses the
/// HTTP boundary — this type is the only thing serialized to clients.
/// </summary>
public sealed class ConfigurationResponse
{
    /// <summary>Server-generated record id.</summary>
    public required string Id { get; init; }

    /// <summary>The configuration key consumers look up.</summary>
    public required string Name { get; init; }

    /// <summary>Declared value type as stored.</summary>
    public required string Type { get; init; }

    /// <summary>Raw stored value.</summary>
    public required string Value { get; init; }

    /// <summary>Whether consuming services currently see this record.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Owning service name.</summary>
    public required string ApplicationName { get; init; }

    /// <summary>Server-stamped UTC timestamp of the last write.</summary>
    public required DateTime LastModifiedDate { get; init; }

    internal static ConfigurationResponse FromRecord(ConfigurationRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        Type = record.Type,
        Value = record.Value,
        IsActive = record.IsActive,
        ApplicationName = record.ApplicationName,
        LastModifiedDate = record.LastModifiedDate,
    };
}
