using System.ComponentModel.DataAnnotations;
using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Contracts;

/// <summary>
/// Shared shape of create/update payloads. <c>Id</c> and <c>LastModifiedDate</c> are
/// server-owned and deliberately have no properties here — a client structurally
/// cannot send them; that guarantee is enforced by the type, not by documentation.
/// </summary>
public abstract class ConfigurationWriteRequest
{
    /// <summary>The configuration key consumers look up, e.g. <c>SiteName</c>.</summary>
    [Required]
    [NotBlank]
    public string Name { get; init; } = string.Empty;

    /// <summary>Declared value type: <c>string</c>, <c>int</c>, <c>double</c> or <c>bool</c>.</summary>
    [Required]
    [NotBlank]
    public string Type { get; init; } = string.Empty;

    /// <summary>Raw value; must be parseable as the declared <c>Type</c> (validated by the service).</summary>
    [Required]
    public string Value { get; init; } = string.Empty;

    /// <summary>Owning service name; consumers only ever see their own records.</summary>
    [Required]
    [NotBlank]
    public string ApplicationName { get; init; } = string.Empty;

    /// <summary>
    /// Tri-state activity flag — the HTTP end of the service's <c>bool? isActive</c>
    /// channel: omitted (<c>null</c>) means the service defaults the record to active.
    /// </summary>
    public bool? IsActive { get; init; }

    /// <summary>Maps the client-writable fields onto a fresh record; nothing else crosses.</summary>
    private protected ConfigurationRecord BuildRecord() => new()
    {
        Name = Name,
        Type = Type,
        Value = Value,
        ApplicationName = ApplicationName,
    };
}
