using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Storage;

/// <summary>
/// The WebUI's own data-access contract — deliberately separate from the library's
/// <c>IConfigurationStorageProvider</c>. The two are different bounded contexts over
/// the same Mongo collection: the library's seam is a consumer contract (one
/// application's records, active only, read-only), while this is an admin contract
/// (every application's records, inactive included, read-write). Merging them would
/// force admin capabilities onto every consuming service; keeping them apart lets
/// each side evolve for its own caller.
/// </summary>
public interface IConfigurationAdminRepository
{
    /// <summary>
    /// Whether <paramref name="id"/> is well-formed for this storage technology.
    /// Lives on the repository because id format is storage knowledge (Mongo:
    /// 24-hex ObjectId) — the service uses it to reject bad ids before any I/O.
    /// </summary>
    bool IsWellFormedId(string? id);

    /// <summary>All records across every application, inactive ones included.</summary>
    Task<IReadOnlyList<ConfigurationRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The record with the given id, or <c>null</c> when none exists.</summary>
    Task<ConfigurationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Inserts the record and returns it with its storage-generated id populated.</summary>
    Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the record with the same id. Returns <c>false</c> when no record has
    /// that id — the business decision of what that means belongs to the service layer.
    /// </summary>
    Task<bool> UpdateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default);
}
