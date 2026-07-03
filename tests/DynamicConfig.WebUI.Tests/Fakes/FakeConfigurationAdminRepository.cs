using DynamicConfig.Library.Models;
using DynamicConfig.WebUI.Storage;

namespace DynamicConfig.WebUI.Tests.Fakes;

/// <summary>
/// Hand-rolled test double for the admin repository seam, following the same idiom
/// as the library tests' FakeConfigurationStorageProvider — no mocking framework for
/// a four-method interface. Stores records in a dictionary and exposes what the
/// service handed it, so tests can assert on the exact persisted state.
/// </summary>
internal sealed class FakeConfigurationAdminRepository : IConfigurationAdminRepository
{
    private readonly Dictionary<string, ConfigurationRecord> _recordsById = new();
    private int _generatedIdCounter;

    /// <summary>The record instance the service last passed to <see cref="CreateAsync"/>.</summary>
    public ConfigurationRecord? LastCreatedRecord { get; private set; }

    /// <summary>The record instance the service last passed to <see cref="UpdateAsync"/>.</summary>
    public ConfigurationRecord? LastUpdatedRecord { get; private set; }

    public void Seed(params ConfigurationRecord[] records)
    {
        foreach (var record in records)
        {
            _recordsById[record.Id] = record;
        }
    }

    public Task<IReadOnlyList<ConfigurationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ConfigurationRecord>>(_recordsById.Values.ToList());
    }

    public Task<ConfigurationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _recordsById.TryGetValue(id, out var record);
        return Task.FromResult(record);
    }

    public Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        LastCreatedRecord = record;
        if (string.IsNullOrEmpty(record.Id))
        {
            record.Id = $"generated-{++_generatedIdCounter}";
        }

        _recordsById[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<bool> UpdateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        LastUpdatedRecord = record;
        if (!_recordsById.ContainsKey(record.Id))
        {
            return Task.FromResult(false);
        }

        _recordsById[record.Id] = record;
        return Task.FromResult(true);
    }
}
