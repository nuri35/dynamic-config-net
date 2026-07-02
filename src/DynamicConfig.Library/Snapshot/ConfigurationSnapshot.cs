using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DynamicConfig.Library.Models;

namespace DynamicConfig.Library.Snapshot;

/// <summary>
/// An immutable, point-in-time view of one application's active configuration records,
/// keyed by record <c>Name</c>. Built once, never mutated — Phase 3's refresh loop will
/// build a fresh instance off to the side and publish it with a single atomic reference
/// swap (ADR 0002), so readers always see a complete old or complete new snapshot.
/// </summary>
/// <remarks>
/// Backed by .NET 8's <see cref="FrozenDictionary{TKey,TValue}"/>: it trades build cost
/// for the fastest possible reads, which is exactly the snapshot trade — built once per
/// refresh, read on every <c>GetValue</c> call.
/// </remarks>
internal sealed class ConfigurationSnapshot
{
    // Case-insensitive lookup mirrors .NET's own IConfiguration semantics and forgives
    // hand-entered record names from the management UI.
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private readonly FrozenDictionary<string, ConfigurationRecord> _recordsByName;

    private ConfigurationSnapshot(FrozenDictionary<string, ConfigurationRecord> recordsByName)
    {
        _recordsByName = recordsByName;
    }

    /// <summary>A snapshot with no records; every lookup misses.</summary>
    internal static ConfigurationSnapshot Empty { get; } =
        new(FrozenDictionary<string, ConfigurationRecord>.Empty);

    /// <summary>
    /// Builds a snapshot from raw storage records. Owns the normalization policy:
    /// blank-named records are dropped, and when the same name appears on several
    /// active records (a management-UI double entry) the latest
    /// <see cref="ConfigurationRecord.LastModifiedDate"/> wins — bad data must
    /// degrade one key, never crash every consumer of the snapshot.
    /// </summary>
    internal static ConfigurationSnapshot Build(IEnumerable<ConfigurationRecord> records)
    {
        var recordsByName = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Name))
            .GroupBy(record => record.Name, KeyComparer)
            .Select(duplicates => duplicates.OrderByDescending(record => record.LastModifiedDate).First())
            .ToFrozenDictionary(record => record.Name, record => record, KeyComparer);

        return new ConfigurationSnapshot(recordsByName);
    }

    internal bool TryGetRecord(string key, [MaybeNullWhen(false)] out ConfigurationRecord record) =>
        _recordsByName.TryGetValue(key, out record);
}
