using DynamicConfig.Library.Models;
using DynamicConfig.Library.Storage.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DynamicConfig.Library.Tests.Storage.Mongo;

public class MongoConfigurationClassMapTests
{
    public MongoConfigurationClassMapTests()
    {
        MongoConfigurationClassMap.EnsureRegistered();
    }

    [Fact]
    public void EnsureRegistered_CalledMultipleTimes_DoesNotThrow()
    {
        // BsonClassMap registration is process-global; a second registration attempt
        // must be a no-op, not an exception (reader + web UI may both construct providers).
        MongoConfigurationClassMap.EnsureRegistered();
        MongoConfigurationClassMap.EnsureRegistered();
    }

    [Fact]
    public void Serialize_Record_StoresIdAsObjectIdWithCaseSchemaElementNames()
    {
        var record = CreateSampleRecord();

        var document = record.ToBsonDocument();

        Assert.Equal(BsonType.ObjectId, document["_id"].BsonType);
        Assert.Equal("SiteName", document["Name"].AsString);
        Assert.Equal("string", document["Type"].AsString);
        Assert.Equal("soty.io", document["Value"].AsString);
        Assert.True(document["IsActive"].AsBoolean);
        Assert.Equal("SERVICE-A", document["ApplicationName"].AsString);
        Assert.Equal(BsonType.DateTime, document["LastModifiedDate"].BsonType);
    }

    [Fact]
    public void Deserialize_Document_RoundTripsAllFields()
    {
        var record = CreateSampleRecord();
        var document = record.ToBsonDocument();

        var deserialized = BsonSerializer.Deserialize<ConfigurationRecord>(document);

        Assert.Equal(record.Id, deserialized.Id);
        Assert.Equal(record.Name, deserialized.Name);
        Assert.Equal(record.Type, deserialized.Type);
        Assert.Equal(record.Value, deserialized.Value);
        Assert.Equal(record.IsActive, deserialized.IsActive);
        Assert.Equal(record.ApplicationName, deserialized.ApplicationName);
        Assert.Equal(record.LastModifiedDate, deserialized.LastModifiedDate);
    }

    [Fact]
    public void Deserialize_LastModifiedDate_ComesBackAsUtc()
    {
        var document = CreateSampleRecord().ToBsonDocument();

        var deserialized = BsonSerializer.Deserialize<ConfigurationRecord>(document);

        Assert.Equal(DateTimeKind.Utc, deserialized.LastModifiedDate.Kind);
    }

    [Fact]
    public void Deserialize_DocumentWithUnknownExtraElement_IsIgnored()
    {
        // Records are hand-managed through the web UI; a stray field added later
        // (or by another tool) must not break every consuming service's deserialization.
        var document = CreateSampleRecord().ToBsonDocument();
        document.Add("FieldAddedInAFutureVersion", "whatever");

        var deserialized = BsonSerializer.Deserialize<ConfigurationRecord>(document);

        Assert.Equal("SiteName", deserialized.Name);
    }

    private static ConfigurationRecord CreateSampleRecord() => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Name = "SiteName",
        Type = "string",
        Value = "soty.io",
        IsActive = true,
        ApplicationName = "SERVICE-A",
        // BSON DateTime is millisecond-precise; use a millisecond-exact value so
        // round-trip equality assertions don't fail on sub-millisecond ticks.
        LastModifiedDate = new DateTime(2026, 7, 2, 10, 30, 0, DateTimeKind.Utc),
    };
}
