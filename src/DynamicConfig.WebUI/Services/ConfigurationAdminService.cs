using DynamicConfig.Library.Conversion;
using DynamicConfig.Library.Models;
using DynamicConfig.WebUI.Exceptions;
using DynamicConfig.WebUI.Storage;

namespace DynamicConfig.WebUI.Services;

/// <summary>
/// Owns every business rule of the admin write path so controllers (Phase 4.2) stay
/// thin. Two rules are load-bearing for the whole system:
/// <list type="bullet">
/// <item>Value-vs-Type validation reuses the library's <see cref="ConfigurationValueParser"/> —
/// the same code that parses on the read path decides what may be written, so the UI
/// can never store a record the readers would choke on. (The library's
/// ConfigurationValueFormatException stays as the read-side net for records written
/// past this service, e.g. straight into Mongo.)</item>
/// <item>This service — not callers, not the repository — stamps
/// <c>LastModifiedDate</c> in UTC on every write: the library's duplicate resolution
/// and change detection order records by that field.</item>
/// </list>
/// </summary>
public sealed class ConfigurationAdminService : IConfigurationAdminService
{
    private readonly IConfigurationAdminRepository _repository;

    public ConfigurationAdminService(IConfigurationAdminRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ConfigurationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    public async Task<ConfigurationRecord> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureWellFormedId(id);

        var record = await _repository.GetByIdAsync(id, cancellationToken);
        return record ?? throw new ConfigurationRecordNotFoundException(id);
    }

    public async Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateBusinessRules(record);

        // Tri-state from the client: omitted means "make it live now" — a config
        // created invisible-by-default surprises operators. Explicit false stays
        // possible for staging a record ahead of activation.
        record.IsActive = isActive ?? true;
        record.LastModifiedDate = DateTime.UtcNow;
        return await _repository.CreateAsync(record, cancellationToken);
    }

    public async Task<ConfigurationRecord> UpdateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureWellFormedId(record.Id);
        ValidateBusinessRules(record);

        record.LastModifiedDate = DateTime.UtcNow;
        var recordWasFound = await _repository.UpdateAsync(record, cancellationToken);
        if (!recordWasFound)
        {
            throw new ConfigurationRecordNotFoundException(record.Id);
        }

        return record;
    }

    /// <summary>
    /// Rejects blank or malformed ids before any storage I/O — a garbage id must
    /// surface as a validation failure (HTTP 400 in 4.2), not leak up as a Mongo
    /// FormatException or masquerade as not-found. The format rule itself comes
    /// from the repository, because id shape is storage knowledge.
    /// </summary>
    private void EnsureWellFormedId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw ConfigurationValidationException.RequiredFieldMissing(nameof(ConfigurationRecord.Id));
        }

        if (!_repository.IsWellFormedId(id))
        {
            throw ConfigurationValidationException.MalformedId(id);
        }
    }

    /// <summary>
    /// The shared create/update rule set: required names, a supported declared Type,
    /// and a Value the readers will actually be able to parse as that Type.
    /// </summary>
    private static void ValidateBusinessRules(ConfigurationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
        {
            throw ConfigurationValidationException.RequiredFieldMissing(nameof(ConfigurationRecord.Name));
        }

        if (string.IsNullOrWhiteSpace(record.ApplicationName))
        {
            throw ConfigurationValidationException.RequiredFieldMissing(nameof(ConfigurationRecord.ApplicationName));
        }

        if (!ConfigurationValueTypes.TryParse(record.Type, out var declaredType))
        {
            throw ConfigurationValidationException.UnsupportedType(record.Type);
        }

        if (!ConfigurationValueParser.IsParseableAs(declaredType, record.Value))
        {
            throw ConfigurationValidationException.ValueTypeMismatch(record.Value, record.Type);
        }
    }
}
