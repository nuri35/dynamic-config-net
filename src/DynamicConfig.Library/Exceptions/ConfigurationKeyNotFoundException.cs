namespace DynamicConfig.Library.Exceptions;

/// <summary>
/// Thrown by <c>GetValue&lt;T&gt;</c> when the requested key has no active record for
/// the calling application. Inactive records and other applications' records are
/// filtered at the storage query, so from the reader's point of view they simply
/// do not exist — they surface as this exception, indistinguishable from a typo.
/// </summary>
public sealed class ConfigurationKeyNotFoundException : DynamicConfigurationException
{
    /// <summary>The key that was requested.</summary>
    public string Key { get; }

    /// <summary>The application whose records were searched.</summary>
    public string ApplicationName { get; }

    /// <summary>Creates the exception for <paramref name="key"/> missing in <paramref name="applicationName"/>'s active records.</summary>
    public ConfigurationKeyNotFoundException(string key, string applicationName)
        : base($"Configuration key '{key}' was not found among the active records of application '{applicationName}'.")
    {
        Key = key;
        ApplicationName = applicationName;
    }
}
