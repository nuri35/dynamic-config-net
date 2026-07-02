using DynamicConfig.Library.Models;
using DynamicConfig.Library.Storage;

namespace DynamicConfig.Library.Tests.Fakes;

/// <summary>
/// Hand-rolled test double for the storage seam — no mocking library needed for a
/// one-method interface. Records what the reader asked for and serves canned data
/// (or throws), so reader tests run with zero MongoDB dependency (ADR 0003).
/// </summary>
internal sealed class FakeConfigurationStorageProvider : IConfigurationStorageProvider
{
    private readonly IReadOnlyList<ConfigurationRecord> _records;
    private readonly Exception? _exceptionToThrow;

    public FakeConfigurationStorageProvider(params ConfigurationRecord[] records)
    {
        _records = records;
    }

    private FakeConfigurationStorageProvider(Exception exceptionToThrow)
    {
        _records = Array.Empty<ConfigurationRecord>();
        _exceptionToThrow = exceptionToThrow;
    }

    public static FakeConfigurationStorageProvider Throwing(Exception exception) => new(exception);

    public string? LastRequestedApplicationName { get; private set; }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<ConfigurationRecord>> GetActiveRecordsAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequestedApplicationName = applicationName;

        return _exceptionToThrow is null
            ? Task.FromResult(_records)
            : Task.FromException<IReadOnlyList<ConfigurationRecord>>(_exceptionToThrow);
    }
}
