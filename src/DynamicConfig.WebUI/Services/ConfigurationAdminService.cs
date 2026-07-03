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
        var record = await _repository.GetByIdAsync(id, cancellationToken);
        return record ?? throw new ConfigurationRecordNotFoundException(id);
    }

    public async Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateBusinessRules(record);

        record.LastModifiedDate = DateTime.UtcNow;
        return await _repository.CreateAsync(record, cancellationToken);
    }

    public async Task<ConfigurationRecord> UpdateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Id))
        {
            throw new ConfigurationValidationException("Id is required to update a configuration record.");
        }

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
    /// The shared create/update rule set: required names, a supported declared Type,
    /// and a Value the readers will actually be able to parse as that Type.
    /// </summary>
    private static void ValidateBusinessRules(ConfigurationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
        {
            throw new ConfigurationValidationException("Name is required and cannot be blank.");
        }

        if (string.IsNullOrWhiteSpace(record.ApplicationName))
        {
            throw new ConfigurationValidationException("ApplicationName is required and cannot be blank.");
        }

        if (!ConfigurationValueTypes.TryParse(record.Type, out var declaredType))
        {
            throw new ConfigurationValidationException(
                $"Type '{record.Type}' is not supported. Supported types: string, int, double, bool.");
        }

        if (!ConfigurationValueParser.IsParseableAs(declaredType, record.Value))
        {
            throw new ConfigurationValidationException(
                $"Value '{record.Value}' cannot be parsed as the declared type '{record.Type}'.");
        }
    }
}
