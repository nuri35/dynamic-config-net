using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Services;

/// <summary>
/// Business rules for managing configuration records. Controllers (Phase 4.2) stay
/// thin: validation, timestamp ownership and not-found semantics all live here.
/// </summary>
public interface IConfigurationAdminService
{
    /// <summary>All records across every application, inactive ones included.</summary>
    Task<IReadOnlyList<ConfigurationRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The record with the given id.</summary>
    /// <exception cref="Exceptions.ConfigurationRecordNotFoundException">No record has that id.</exception>
    Task<ConfigurationRecord> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Validates and stores a new record, stamping <c>LastModifiedDate</c> (UTC).</summary>
    /// <param name="record">The record to store. Its <c>IsActive</c> field is ignored —
    /// <paramref name="isActive"/> is the only channel for that flag.</param>
    /// <param name="isActive">The client's tri-state activity choice: <c>null</c> means
    /// "not provided" and defaults to <c>true</c>. A separate nullable parameter because
    /// the case-schema record's <c>IsActive</c> is a non-nullable <c>bool</c> and cannot
    /// express omission.</param>
    /// <param name="cancellationToken">Cancels the storage write.</param>
    /// <exception cref="Exceptions.ConfigurationValidationException">A business rule failed.</exception>
    Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, bool? isActive = null, CancellationToken cancellationToken = default);

    /// <summary>Validates and replaces an existing record, refreshing <c>LastModifiedDate</c> (UTC).</summary>
    /// <param name="record">The replacement state. Its <c>IsActive</c> field is ignored —
    /// <paramref name="isActive"/> is the only channel, exactly as on create.</param>
    /// <param name="isActive">The client's tri-state activity choice: <c>null</c> means
    /// "not provided" and defaults to <c>true</c> — the same rule as create, so both
    /// write verbs share one documented default.</param>
    /// <param name="cancellationToken">Cancels the storage write.</param>
    /// <exception cref="Exceptions.ConfigurationValidationException">A business rule failed.</exception>
    /// <exception cref="Exceptions.ConfigurationRecordNotFoundException">No record has that id.</exception>
    Task<ConfigurationRecord> UpdateAsync(ConfigurationRecord record, bool? isActive = null, CancellationToken cancellationToken = default);
}
