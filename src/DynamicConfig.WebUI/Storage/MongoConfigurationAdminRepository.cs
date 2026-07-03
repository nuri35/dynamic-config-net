using DynamicConfig.Library.Models;
using DynamicConfig.Library.Storage.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DynamicConfig.WebUI.Storage;

/// <summary>
/// MongoDB implementation of the admin contract. Reads and writes the exact
/// collection the library's <c>MongoConfigurationStorageProvider</c> reads —
/// collection name and BSON mapping are shared via the library's public
/// <see cref="MongoConfigurationStorageDefaults"/> and
/// <see cref="MongoConfigurationClassMap"/>, so the two bounded contexts can
/// never drift apart on storage details.
/// </summary>
public sealed class MongoConfigurationAdminRepository : IConfigurationAdminRepository
{
    private readonly IMongoCollection<ConfigurationRecord> _configurationRecords;

    public MongoConfigurationAdminRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        MongoConfigurationClassMap.EnsureRegistered();
        _configurationRecords = database.GetCollection<ConfigurationRecord>(
            MongoConfigurationStorageDefaults.CollectionName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigurationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Admin view: no ApplicationName/IsActive filtering — that narrowing is the
        // library's consumer contract, not this one's.
        return await _configurationRecords
            .Find(Builders<ConfigurationRecord>.Filter.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ConfigurationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // A malformed id cannot match any stored ObjectId — treat it as not-found
        // instead of letting the filter builder throw a FormatException.
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        return await _configurationRecords
            .Find(BuildIdFilter(id))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ConfigurationRecord> CreateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // The shared class map's StringObjectIdGenerator populates record.Id during
        // the insert, so the caller gets the storage-generated id back.
        await _configurationRecords
            .InsertOneAsync(record, options: null, cancellationToken)
            .ConfigureAwait(false);

        return record;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(ConfigurationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!ObjectId.TryParse(record.Id, out _))
        {
            return false;
        }

        var replaceResult = await _configurationRecords
            .ReplaceOneAsync(BuildIdFilter(record.Id), record, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // MatchedCount (not ModifiedCount): saving a record without changes is still
        // a successful update, not a not-found.
        return replaceResult.MatchedCount > 0;
    }

    /// <summary>
    /// Renders as <c>{ _id: ObjectId(id) }</c> under the shared class map — the
    /// rendered-filter unit test pins that, because a string-typed comparison
    /// would silently match nothing.
    /// </summary>
    internal static FilterDefinition<ConfigurationRecord> BuildIdFilter(string id)
    {
        return Builders<ConfigurationRecord>.Filter.Eq(record => record.Id, id);
    }
}
