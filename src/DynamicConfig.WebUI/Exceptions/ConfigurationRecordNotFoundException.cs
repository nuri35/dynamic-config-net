namespace DynamicConfig.WebUI.Exceptions;

/// <summary>
/// Thrown by the admin service when an operation targets a record id that does not
/// exist (deleted meanwhile, or a stale/foreign id). Phase 4.2 maps this to HTTP 404.
/// </summary>
public sealed class ConfigurationRecordNotFoundException : Exception
{
    public ConfigurationRecordNotFoundException(string recordId)
        : base($"No configuration record exists with id '{recordId}'.")
    {
        RecordId = recordId;
    }

    public string RecordId { get; }
}
