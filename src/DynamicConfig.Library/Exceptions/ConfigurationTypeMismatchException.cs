namespace DynamicConfig.Library.Exceptions;

/// <summary>
/// Thrown when the type requested via <c>GetValue&lt;T&gt;</c> does not match the
/// record's declared <c>Type</c>. Matching is strict — no widening (an int-typed
/// record cannot be read as double): the declared type is the record's contract,
/// and silently widening would hide authoring mistakes in the management UI.
/// </summary>
public sealed class ConfigurationTypeMismatchException : DynamicConfigurationException
{
    /// <summary>The key whose record was accessed.</summary>
    public string Key { get; }

    /// <summary>The record's declared type, as stored (e.g. <c>"int"</c>).</summary>
    public string DeclaredType { get; }

    /// <summary>The type the caller requested (e.g. <c>"Double"</c>).</summary>
    public string RequestedType { get; }

    /// <summary>Creates the exception for a request of <paramref name="requestedType"/> against a record declared as <paramref name="declaredType"/>.</summary>
    public ConfigurationTypeMismatchException(string key, string declaredType, string requestedType)
        : base($"Configuration key '{key}' declares type '{declaredType}' and cannot be served as requested type '{requestedType}'. Supported types: string, int, double, bool; no implicit widening.")
    {
        Key = key;
        DeclaredType = declaredType;
        RequestedType = requestedType;
    }
}
