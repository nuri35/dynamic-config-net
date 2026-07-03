using DynamicConfig.Library.Models;
using DynamicConfig.WebUI.Services;

namespace DynamicConfig.WebUI.Tests.Fakes;

/// <summary>
/// Hand-rolled double for the service seam. Records exactly what the controller
/// passed (the whole point of controller tests: right method, right arguments) and
/// serves canned data. Can throw on demand to prove actions do not catch.
/// </summary>
internal sealed class FakeConfigurationAdminService : IConfigurationAdminService
{
    public const string GeneratedId = "65f1a2b3c4d5e6f7a8b9c0ff";
    public static readonly DateTime StampedDate = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly List<ConfigurationRecord> _records = new();

    public ConfigurationRecord? LastCreatedRecord { get; private set; }
    public bool? LastCreateIsActive { get; private set; }
    public ConfigurationRecord? LastUpdatedRecord { get; private set; }
    public string? LastRequestedId { get; private set; }
    public Exception? ExceptionToThrow { get; set; }

    public void Seed(params ConfigurationRecord[] records) => _records.AddRange(records);

    public Task<IReadOnlyList<ConfigurationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult<IReadOnlyList<ConfigurationRecord>>(_records);
    }

    public Task<ConfigurationRecord> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        LastRequestedId = id;
        ThrowIfConfigured();
        return Task.FromResult(_records.Single(record => record.Id == id));
    }

    public Task<ConfigurationRecord> CreateAsync(
        ConfigurationRecord record,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        LastCreatedRecord = record;
        LastCreateIsActive = isActive;
        ThrowIfConfigured();

        // Mimic the real service/storage outcome so the controller has server-owned
        // fields to map into the response.
        record.Id = GeneratedId;
        record.IsActive = isActive ?? true;
        record.LastModifiedDate = StampedDate;
        return Task.FromResult(record);
    }

    public Task<ConfigurationRecord> UpdateAsync(
        ConfigurationRecord record,
        CancellationToken cancellationToken = default)
    {
        LastUpdatedRecord = record;
        ThrowIfConfigured();

        record.LastModifiedDate = StampedDate;
        return Task.FromResult(record);
    }

    private void ThrowIfConfigured()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}
