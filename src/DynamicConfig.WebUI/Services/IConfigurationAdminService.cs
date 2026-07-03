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
    /// <exception cref="Exceptions.ConfigurationValidationException">A business rule failed.</exception>
    Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default);

    /// <summary>Validates and replaces an existing record, refreshing <c>LastModifiedDate</c> (UTC).</summary>
    /// <exception cref="Exceptions.ConfigurationValidationException">A business rule failed.</exception>
    /// <exception cref="Exceptions.ConfigurationRecordNotFoundException">No record has that id.</exception>
    Task<ConfigurationRecord> UpdateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default);
}
