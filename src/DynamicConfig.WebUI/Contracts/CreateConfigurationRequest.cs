using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Contracts;

/// <summary>Payload for <c>POST /api/configurations</c>.</summary>
public sealed class CreateConfigurationRequest : ConfigurationWriteRequest
{
    /// <summary>The storage generates the id; the service stamps the timestamp and activity.</summary>
    internal ConfigurationRecord ToRecord() => BuildRecord();
}
