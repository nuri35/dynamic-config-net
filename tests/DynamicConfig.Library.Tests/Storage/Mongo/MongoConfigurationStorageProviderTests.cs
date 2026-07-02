using DynamicConfig.Library.Models;
using DynamicConfig.Library.Storage.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DynamicConfig.Library.Tests.Storage.Mongo;

/// <summary>
/// Unit tests for the provider's real logic: filter construction, database-name
/// resolution and argument guards. Talking to an actual MongoDB is integration
/// scope (deferred to the e2e phase); mocking IMongoCollection internals is low
/// value and deliberately avoided.
/// </summary>
public class MongoConfigurationStorageProviderTests
{
    private const string AnyReachableConnectionString = "mongodb://localhost:27017";

    [Fact]
    public void BuildActiveRecordsFilter_FiltersOnApplicationNameAndIsActiveAtQueryLevel()
    {
        var filter = MongoConfigurationStorageProvider.BuildActiveRecordsFilter("SERVICE-A");

        var renderedQuery = Render(filter);

        // The hard isolation requirement of the case: both conditions must be part of
        // the Mongo query document itself, never post-filtered in memory.
        var expected = new BsonDocument
        {
            { "ApplicationName", "SERVICE-A" },
            { "IsActive", true },
        };
        Assert.Equal(expected, renderedQuery);
    }

    [Theory]
    [InlineData("mongodb://localhost:27017/config-db", "config-db")]
    [InlineData("mongodb://user:pw@mongo-host:27017/DynamicConfig?authSource=admin", "DynamicConfig")]
    public void ResolveDatabaseName_ConnectionStringNamesADatabase_UsesThatDatabase(
        string connectionString, string expectedDatabaseName)
    {
        var databaseName = MongoConfigurationStorageProvider.ResolveDatabaseName(MongoUrl.Create(connectionString));

        Assert.Equal(expectedDatabaseName, databaseName);
    }

    [Fact]
    public void ResolveDatabaseName_ConnectionStringWithoutDatabase_FallsBackToDefault()
    {
        var databaseName = MongoConfigurationStorageProvider.ResolveDatabaseName(
            MongoUrl.Create(AnyReachableConnectionString));

        Assert.Equal(MongoConfigurationStorageDefaults.DatabaseName, databaseName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankConnectionString_ThrowsArgumentException(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new MongoConfigurationStorageProvider(connectionString));
    }

    [Fact]
    public void Constructor_NullConnectionString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MongoConfigurationStorageProvider(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetActiveRecordsAsync_BlankApplicationName_ThrowsArgumentException(string applicationName)
    {
        // MongoClient connects lazily, so constructing the provider and hitting the
        // argument guard never requires a running MongoDB.
        var provider = new MongoConfigurationStorageProvider(AnyReachableConnectionString);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetActiveRecordsAsync(applicationName));
    }

    private static BsonDocument Render(FilterDefinition<ConfigurationRecord> filter)
    {
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var documentSerializer = serializerRegistry.GetSerializer<ConfigurationRecord>();
        return filter.Render(new RenderArgs<ConfigurationRecord>(documentSerializer, serializerRegistry));
    }
}
