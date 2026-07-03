using DynamicConfig.Library.Models;
using DynamicConfig.Library.Storage.Mongo;
using DynamicConfig.WebUI.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DynamicConfig.WebUI.Tests.Storage;

/// <summary>
/// Unit tests for the repository's real logic — filter construction. Mongo CRUD
/// plumbing is deliberately untested here (mocking IMongoCollection internals is
/// low value); the end-to-end behavior is covered when the UI runs against the
/// compose stack.
/// </summary>
public class MongoConfigurationAdminRepositoryTests
{
    public MongoConfigurationAdminRepositoryTests()
    {
        // The id filter only renders correctly under the shared class map
        // (string Id ↔ ObjectId) — the exact mapping production uses.
        MongoConfigurationClassMap.EnsureRegistered();
    }

    [Fact]
    public void BuildIdFilter_RendersIdAsObjectIdAgainstMongoIdField()
    {
        var recordId = "65f1a2b3c4d5e6f7a8b9c0d1";

        var renderedQuery = Render(MongoConfigurationAdminRepository.BuildIdFilter(recordId));

        // Must target Mongo's _id field with a real ObjectId — a string-typed
        // comparison would silently match nothing.
        var expected = new BsonDocument { { "_id", ObjectId.Parse(recordId) } };
        Assert.Equal(expected, renderedQuery);
    }

    private static BsonDocument Render(FilterDefinition<ConfigurationRecord> filter)
    {
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var documentSerializer = serializerRegistry.GetSerializer<ConfigurationRecord>();
        return filter.Render(new RenderArgs<ConfigurationRecord>(documentSerializer, serializerRegistry));
    }
}
