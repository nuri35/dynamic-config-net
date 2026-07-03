using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Contracts;

/// <summary>Payload for <c>PUT /api/configurations/{id}</c>.</summary>
public sealed class UpdateConfigurationRequest : ConfigurationWriteRequest
{
    /// <summary>
    /// The target id comes exclusively from the route — the body cannot name a
    /// different record than the URL addresses.
    /// </summary>
    internal ConfigurationRecord ToRecord(string id)
    {
        var record = BuildRecord();
        record.Id = id;
        return record;
    }
}
