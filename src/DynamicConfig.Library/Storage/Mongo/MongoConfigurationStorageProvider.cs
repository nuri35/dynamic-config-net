using DynamicConfig.Library.Models;
using MongoDB.Driver;

namespace DynamicConfig.Library.Storage.Mongo;

/// <summary>
/// MongoDB implementation of <see cref="IConfigurationStorageProvider"/>.
/// Filtering on <c>(ApplicationName, IsActive)</c> happens inside the Mongo query —
/// another service's records never enter this process — and is backed by a compound
/// index created idempotently on first use.
/// </summary>
public sealed class MongoConfigurationStorageProvider : IConfigurationStorageProvider
{
    private readonly IMongoCollection<ConfigurationRecord> _configurationRecords;

    // .NET services handle requests on many threads (no single-threaded event loop as
    // in Node), so the run-once index bootstrap needs real synchronization: a volatile
    // completion flag plus an async-compatible lock (SemaphoreSlim — the C# `lock`
    // statement cannot contain `await`).
    private readonly SemaphoreSlim _indexCreationLock = new(1, 1);
    private volatile bool _compoundIndexEnsured;

    /// <summary>
    /// Creates a provider from a MongoDB connection string. If the connection string
    /// names a database (e.g. <c>mongodb://host:27017/my-db</c>) that database is used;
    /// otherwise <see cref="MongoConfigurationStorageDefaults.DatabaseName"/> applies.
    /// </summary>
    /// <remarks>
    /// Construction never touches the network — MongoClient connects lazily on the
    /// first operation, which keeps "storage unreachable" a runtime concern the
    /// reader's refresh loop can survive (Phase 3).
    /// </remarks>
    public MongoConfigurationStorageProvider(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        MongoConfigurationClassMap.EnsureRegistered();

        var connectionUrl = MongoUrl.Create(connectionString);
        var client = new MongoClient(connectionUrl);
        var database = client.GetDatabase(ResolveDatabaseName(connectionUrl));
        _configurationRecords = database.GetCollection<ConfigurationRecord>(
            MongoConfigurationStorageDefaults.CollectionName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigurationRecord>> GetActiveRecordsAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        await EnsureCompoundIndexCreatedAsync(cancellationToken).ConfigureAwait(false);

        return await _configurationRecords
            .Find(BuildActiveRecordsFilter(applicationName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The isolation contract of the case, expressed as a Mongo query document:
    /// both conditions are evaluated by the server, never in application memory.
    /// </summary>
    internal static FilterDefinition<ConfigurationRecord> BuildActiveRecordsFilter(string applicationName)
    {
        var filter = Builders<ConfigurationRecord>.Filter;
        return filter.And(
            filter.Eq(record => record.ApplicationName, applicationName),
            filter.Eq(record => record.IsActive, true));
    }

    internal static string ResolveDatabaseName(MongoUrl connectionUrl)
    {
        return string.IsNullOrWhiteSpace(connectionUrl.DatabaseName)
            ? MongoConfigurationStorageDefaults.DatabaseName
            : connectionUrl.DatabaseName;
    }

    /// <summary>
    /// Creates the <c>(ApplicationName, IsActive)</c> compound index once per provider
    /// lifetime. Mongo's CreateOne is itself idempotent (same name + same keys is a
    /// no-op server-side), so concurrent providers cannot conflict; the flag only
    /// spares the extra round-trip on every read.
    /// </summary>
    private async Task EnsureCompoundIndexCreatedAsync(CancellationToken cancellationToken)
    {
        if (_compoundIndexEnsured)
        {
            return;
        }

        await _indexCreationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_compoundIndexEnsured)
            {
                return;
            }

            var compoundIndex = new CreateIndexModel<ConfigurationRecord>(
                Builders<ConfigurationRecord>.IndexKeys
                    .Ascending(record => record.ApplicationName)
                    .Ascending(record => record.IsActive),
                new CreateIndexOptions { Name = MongoConfigurationStorageDefaults.ApplicationNameIsActiveIndexName });

            await _configurationRecords.Indexes
                .CreateOneAsync(compoundIndex, options: null, cancellationToken)
                .ConfigureAwait(false);

            // Only set on success: if Mongo was down, the next call retries instead of
            // permanently skipping index creation.
            _compoundIndexEnsured = true;
        }
        finally
        {
            _indexCreationLock.Release();
        }
    }
}
