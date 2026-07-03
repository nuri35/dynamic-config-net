namespace DynamicConfig.WebUI.Exceptions;

/// <summary>
/// Thrown by the admin service when a record fails a business rule before it is
/// written (blank Name/ApplicationName, unsupported Type, or a Value that cannot
/// be parsed as the declared Type). This is the write-side half of the safety
/// story: the library's <c>ConfigurationValueFormatException</c> remains the
/// read-side net for records that bypass the UI. Phase 4.2 maps this to HTTP 400.
/// </summary>
public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(string message)
        : base(message)
    {
    }
}
