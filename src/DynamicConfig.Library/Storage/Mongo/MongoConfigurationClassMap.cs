using DynamicConfig.Library.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;

namespace DynamicConfig.Library.Storage.Mongo;

/// <summary>
/// Registers the BSON mapping for <see cref="ConfigurationRecord"/>.
/// Mapping is code-based (BsonClassMap) instead of attributes so the domain model
/// stays free of MongoDB dependencies (see ADR 0003).
/// </summary>
internal static class MongoConfigurationClassMap
{
    /// <summary>
    /// Registers the class map exactly once per process. Safe to call from multiple
    /// threads and multiple provider instances — unlike Node's single-threaded module
    /// init, .NET static state can be raced, so we rely on the driver's thread-safe
    /// TryRegisterClassMap rather than a check-then-register of our own.
    /// </summary>
    internal static void EnsureRegistered()
    {
        BsonClassMap.TryRegisterClassMap<ConfigurationRecord>(classMap =>
        {
            classMap.AutoMap();

            // The domain model exposes Id as string (provider-agnostic), but it is
            // persisted as a real ObjectId so Mongo generates ids on insert.
            classMap.MapIdMember(record => record.Id)
                .SetSerializer(new StringSerializer(BsonType.ObjectId))
                .SetIdGenerator(StringObjectIdGenerator.Instance);

            // Always round-trip as UTC; the default serializer would otherwise hand
            // back DateTimeKind.Utc values only by convention, not by contract.
            classMap.MapMember(record => record.LastModifiedDate)
                .SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));

            // A field added to documents later (new library version, external tool)
            // must not break older consumers' deserialization.
            classMap.SetIgnoreExtraElements(true);
        });
    }
}
