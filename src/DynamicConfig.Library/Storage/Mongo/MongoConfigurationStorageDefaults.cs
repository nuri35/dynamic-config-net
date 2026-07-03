namespace DynamicConfig.Library.Storage.Mongo;

/// <summary>
/// Storage-layer constants — kept in one place so no collection/database/index name
/// is ever a magic string at a call site. Public because the WebUI's admin repository
/// writes to the very same collection the library reads: sharing the constant makes a
/// collection-name mismatch (which would silently break the end-to-end story)
/// impossible at compile time.
/// </summary>
public static class MongoConfigurationStorageDefaults
{
    /// <summary>Used when the connection string does not name a database.</summary>
    public const string DatabaseName = "DynamicConfigDb";

    /// <summary>Single collection holding every application's configuration records.</summary>
    public const string CollectionName = "ConfigurationRecords";

    /// <summary>Compound index backing the (ApplicationName, IsActive) query.</summary>
    public const string ApplicationNameIsActiveIndexName = "ApplicationName_IsActive";
}
