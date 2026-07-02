namespace DynamicConfig.Library.Storage.Mongo;

/// <summary>
/// Storage-layer constants — kept in one place so no collection/database/index name
/// is ever a magic string at a call site.
/// </summary>
internal static class MongoConfigurationStorageDefaults
{
    /// <summary>Used when the connection string does not name a database.</summary>
    internal const string DatabaseName = "DynamicConfigDb";

    /// <summary>Single collection holding every application's configuration records.</summary>
    internal const string CollectionName = "ConfigurationRecords";

    /// <summary>Compound index backing the (ApplicationName, IsActive) query.</summary>
    internal const string ApplicationNameIsActiveIndexName = "ApplicationName_IsActive";
}
