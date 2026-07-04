namespace DynamicConfig.Library.Exceptions;

/// <summary>
/// Thrown when a record's stored <c>Value</c> cannot be converted to its own declared
/// <c>Type</c> (e.g. Type=int, Value="abc"). This is data corruption, not a caller
/// mistake — deliberately distinct from <see cref="ConfigurationTypeMismatchException"/>
/// so operators know the fix is editing the record, not changing calling code.
/// </summary>
public sealed class ConfigurationValueFormatException : DynamicConfigurationException
{
    /// <summary>The key whose record holds the corrupt value.</summary>
    public string Key { get; }

    /// <summary>The record's declared type, as stored.</summary>
    public string DeclaredType { get; }

    /// <summary>The raw stored value that failed to convert.</summary>
    public string RawValue { get; }

    /// <summary>Creates the exception for <paramref name="rawValue"/> failing to convert to the record's declared <paramref name="declaredType"/>.</summary>
    public ConfigurationValueFormatException(string key, string declaredType, string rawValue)
        : base($"Configuration key '{key}' declares type '{declaredType}' but its stored value '{rawValue}' cannot be converted to that type. Fix the record's Value in the management UI.")
    {
        Key = key;
        DeclaredType = declaredType;
        RawValue = rawValue;
    }
}
