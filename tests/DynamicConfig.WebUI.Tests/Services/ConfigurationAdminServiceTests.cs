using DynamicConfig.Library.Models;
using DynamicConfig.WebUI.Exceptions;
using DynamicConfig.WebUI.Services;
using DynamicConfig.WebUI.Tests.Fakes;

namespace DynamicConfig.WebUI.Tests.Services;

public class ConfigurationAdminServiceTests
{
    // Well-formed ObjectIds: id-format validation happens before existence checks,
    // so "exists" and "unknown" scenarios both need storage-valid ids.
    private const string ExistingId = "65f1a2b3c4d5e6f7a8b9c0d1";
    private const string UnknownId = "65f1a2b3c4d5e6f7a8b9c0d2";

    private readonly FakeConfigurationAdminRepository _repository = new();
    private readonly ConfigurationAdminService _service;

    public ConfigurationAdminServiceTests()
    {
        _service = new ConfigurationAdminService(_repository);
    }

    private static ConfigurationRecord BuildValidRecord(
        string id = "",
        string name = "SiteName",
        string type = "string",
        string value = "soty.io",
        string applicationName = "SERVICE-A")
    {
        return new ConfigurationRecord
        {
            Id = id,
            Name = name,
            Type = type,
            Value = value,
            IsActive = true,
            ApplicationName = applicationName,
        };
    }

    // --- Create: happy path per supported type -------------------------------

    [Theory]
    [InlineData("string", "soty.io")]
    [InlineData("int", "42")]
    [InlineData("Int", "50")] // exact casing used in the case PDF's sample data
    [InlineData("double", "1.5")]
    [InlineData("bool", "true")]
    [InlineData("boolean", "1")] // the sample data stores booleans as 1/0
    public async Task CreateAsync_ValueMatchingDeclaredType_PersistsRecord(string type, string value)
    {
        var record = BuildValidRecord(type: type, value: value);

        var created = await _service.CreateAsync(record);

        Assert.NotNull(_repository.LastCreatedRecord);
        Assert.Equal(value, _repository.LastCreatedRecord!.Value);
        Assert.Equal(created.Id, _repository.LastCreatedRecord.Id);
    }

    // --- Create: validation matrix -------------------------------------------

    [Theory]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_UnsupportedType_ThrowsValidation(string type)
    {
        var record = BuildValidRecord(type: type);

        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.CreateAsync(record));

        Assert.Equal(nameof(ConfigurationRecord.Type), exception.FieldName);
        // The supported list is derived from the enum — the message can never lie
        // about what the library actually accepts.
        Assert.Contains("string, int, double, bool", exception.Message);
    }

    [Theory]
    [InlineData("int", "abc")]
    [InlineData("int", "1.5")] // fractional input is not an int
    [InlineData("double", "not-a-number")]
    [InlineData("double", "1,5")] // tr-TR style comma separator must be rejected
    [InlineData("bool", "yes")]
    [InlineData("bool", "2")]
    public async Task CreateAsync_ValueNotParseableAsDeclaredType_ThrowsValidation(string type, string value)
    {
        var record = BuildValidRecord(type: type, value: value);

        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.CreateAsync(record));

        // The message must carry enough context for the UI to show a useful error,
        // and FieldName lets 4.2 attach it to the right form field.
        Assert.Contains(value, exception.Message);
        Assert.Contains(type, exception.Message);
        Assert.Equal(nameof(ConfigurationRecord.Value), exception.FieldName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_ThrowsValidation(string name)
    {
        var record = BuildValidRecord(name: name);

        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.CreateAsync(record));

        Assert.Equal(nameof(ConfigurationRecord.Name), exception.FieldName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankApplicationName_ThrowsValidation(string applicationName)
    {
        var record = BuildValidRecord(applicationName: applicationName);

        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.CreateAsync(record));

        Assert.Equal(nameof(ConfigurationRecord.ApplicationName), exception.FieldName);
    }

    [Fact]
    public async Task CreateAsync_NullRecord_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateAsync(null!));
    }

    [Fact]
    public async Task CreateAsync_InvalidRecord_NeverReachesRepository()
    {
        var record = BuildValidRecord(type: "int", value: "abc");

        await Assert.ThrowsAsync<ConfigurationValidationException>(() => _service.CreateAsync(record));

        Assert.Null(_repository.LastCreatedRecord);
    }

    // --- Create: IsActive tri-state --------------------------------------------

    [Fact]
    public async Task CreateAsync_IsActiveNotProvided_DefaultsToTrue()
    {
        // A record the client created without an explicit choice should take effect
        // immediately — an invisible-by-default config surprises operators.
        var record = BuildValidRecord();
        record.IsActive = false; // whatever the record carries is ignored; the parameter is the channel

        await _service.CreateAsync(record);

        Assert.True(_repository.LastCreatedRecord!.IsActive);
    }

    [Fact]
    public async Task CreateAsync_IsActiveExplicitlyFalse_StoresInactiveRecord()
    {
        var record = BuildValidRecord();

        await _service.CreateAsync(record, isActive: false);

        Assert.False(_repository.LastCreatedRecord!.IsActive);
    }

    [Fact]
    public async Task CreateAsync_IsActiveExplicitlyTrue_StoresActiveRecord()
    {
        var record = BuildValidRecord();
        record.IsActive = false;

        await _service.CreateAsync(record, isActive: true);

        Assert.True(_repository.LastCreatedRecord!.IsActive);
    }

    // --- Create: timestamp ownership ------------------------------------------

    [Fact]
    public async Task CreateAsync_StampsLastModifiedDateWithCurrentUtc()
    {
        var record = BuildValidRecord();
        var beforeCreate = DateTime.UtcNow;

        await _service.CreateAsync(record);

        var afterCreate = DateTime.UtcNow;
        var stamped = _repository.LastCreatedRecord!.LastModifiedDate;
        Assert.Equal(DateTimeKind.Utc, stamped.Kind);
        Assert.InRange(stamped, beforeCreate, afterCreate);
    }

    // --- Update ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ExistingRecord_PersistsChangedFields()
    {
        _repository.Seed(BuildValidRecord(id: ExistingId, value: "old-value"));
        var updated = BuildValidRecord(id: ExistingId, value: "new-value");

        await _service.UpdateAsync(updated);

        Assert.Equal("new-value", _repository.LastUpdatedRecord!.Value);
    }

    [Fact]
    public async Task UpdateAsync_RefreshesLastModifiedDateWithCurrentUtc()
    {
        // Load-bearing behavior: the library's duplicate resolution and change
        // detection order records by LastModifiedDate — a stale timestamp on update
        // would make consumers keep serving the OLD value. This test pins the rule.
        var staleDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _repository.Seed(BuildValidRecord(id: ExistingId));
        var updated = BuildValidRecord(id: ExistingId);
        updated.LastModifiedDate = staleDate;
        var beforeUpdate = DateTime.UtcNow;

        await _service.UpdateAsync(updated);

        var afterUpdate = DateTime.UtcNow;
        var stamped = _repository.LastUpdatedRecord!.LastModifiedDate;
        Assert.Equal(DateTimeKind.Utc, stamped.Kind);
        Assert.InRange(stamped, beforeUpdate, afterUpdate);
    }

    // --- Update: IsActive tri-state (same channel as create — a smoke-caught gap:
    // without it, every update silently persisted default(bool) = inactive) --------

    [Fact]
    public async Task UpdateAsync_IsActiveNotProvided_DefaultsToTrue()
    {
        _repository.Seed(BuildValidRecord(id: ExistingId));
        var updated = BuildValidRecord(id: ExistingId);
        updated.IsActive = false; // the record's own field is ignored; the parameter is the channel

        await _service.UpdateAsync(updated);

        Assert.True(_repository.LastUpdatedRecord!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_IsActiveExplicitlyFalse_StoresInactiveRecord()
    {
        _repository.Seed(BuildValidRecord(id: ExistingId));
        var updated = BuildValidRecord(id: ExistingId);

        await _service.UpdateAsync(updated, isActive: false);

        Assert.False(_repository.LastUpdatedRecord!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_IsActiveExplicitlyTrue_StoresActiveRecord()
    {
        _repository.Seed(BuildValidRecord(id: ExistingId));
        var updated = BuildValidRecord(id: ExistingId);
        updated.IsActive = false;

        await _service.UpdateAsync(updated, isActive: true);

        Assert.True(_repository.LastUpdatedRecord!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsRecordNotFound()
    {
        var record = BuildValidRecord(id: UnknownId);

        var exception = await Assert.ThrowsAsync<ConfigurationRecordNotFoundException>(
            () => _service.UpdateAsync(record));

        Assert.Equal(UnknownId, exception.RecordId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_BlankId_ThrowsValidation(string id)
    {
        var record = BuildValidRecord(id: id);

        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.UpdateAsync(record));

        Assert.Equal(nameof(ConfigurationRecord.Id), exception.FieldName);
    }

    [Fact]
    public async Task UpdateAsync_MalformedId_ThrowsValidationNotMongoError()
    {
        // A garbage id must be caught by service validation — not leak up as a
        // storage FormatException, and not masquerade as a 404-style not-found.
        var record = BuildValidRecord(id: "not-an-objectid");

        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.UpdateAsync(record));

        Assert.Equal(nameof(ConfigurationRecord.Id), exception.FieldName);

        Assert.Null(_repository.LastUpdatedRecord); // rejected before any storage call
    }

    [Fact]
    public async Task UpdateAsync_ValueNotParseableAsDeclaredType_ThrowsValidation()
    {
        _repository.Seed(BuildValidRecord(id: ExistingId));
        var updated = BuildValidRecord(id: ExistingId, type: "int", value: "abc");

        await Assert.ThrowsAsync<ConfigurationValidationException>(() => _service.UpdateAsync(updated));

        Assert.Null(_repository.LastUpdatedRecord); // rejected before any write
    }

    [Fact]
    public async Task UpdateAsync_NullRecord_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateAsync(null!));
    }

    // --- Reads -------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsEveryRecordIncludingInactiveAndForeignApplications()
    {
        var activeRecord = BuildValidRecord(id: "a", applicationName: "SERVICE-A");
        var inactiveRecord = BuildValidRecord(id: "b", applicationName: "SERVICE-B");
        inactiveRecord.IsActive = false;
        _repository.Seed(activeRecord, inactiveRecord);

        var all = await _service.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, record => record.Id == "b" && !record.IsActive);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingRecord_ReturnsIt()
    {
        _repository.Seed(BuildValidRecord(id: ExistingId));

        var found = await _service.GetByIdAsync(ExistingId);

        Assert.Equal(ExistingId, found.Id);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsRecordNotFound()
    {
        await Assert.ThrowsAsync<ConfigurationRecordNotFoundException>(
            () => _service.GetByIdAsync(UnknownId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-objectid")]
    [InlineData("65f1a2b3")] // too short for a 24-hex ObjectId
    public async Task GetByIdAsync_BlankOrMalformedId_ThrowsValidationNotMongoError(string id)
    {
        var exception = await Assert.ThrowsAsync<ConfigurationValidationException>(
            () => _service.GetByIdAsync(id));

        Assert.Equal(nameof(ConfigurationRecord.Id), exception.FieldName);
    }
}
