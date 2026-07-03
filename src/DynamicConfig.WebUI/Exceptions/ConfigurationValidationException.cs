using DynamicConfig.Library.Models;

namespace DynamicConfig.WebUI.Exceptions;

/// <summary>
/// Thrown by the admin service when a record fails a business rule before it is
/// written. This is the write-side half of the safety story: the library's
/// <c>ConfigurationValueFormatException</c> remains the read-side net for records
/// that bypass the UI. Phase 4.2 maps this to HTTP 400.
/// <para>
/// The constructor is private on purpose: every validation failure is created
/// through a named factory, so messages live in exactly one place and a bare
/// <c>new ConfigurationValidationException("invalid")</c> cannot exist.
/// </para>
/// </summary>
public sealed class ConfigurationValidationException : Exception
{
    // Derived from the enum so the message can never drift from what the library
    // actually accepts if a type is ever added.
    private static readonly string SupportedTypeNames =
        string.Join(", ", Enum.GetNames<ConfigurationValueType>()).ToLowerInvariant();

    private ConfigurationValidationException(string fieldName, string message)
        : base(message)
    {
        FieldName = fieldName;
    }

    /// <summary>
    /// The <see cref="ConfigurationRecord"/> field that failed validation —
    /// lets the 4.2 UI attach the error to the matching form field.
    /// </summary>
    public string FieldName { get; }

    /// <summary>A required field (Name, ApplicationName, Id) was null or blank.</summary>
    public static ConfigurationValidationException RequiredFieldMissing(string fieldName) =>
        new(fieldName, $"{fieldName} is required and cannot be blank.");

    /// <summary>The id is not a valid record id for the underlying storage.</summary>
    public static ConfigurationValidationException MalformedId(string id) =>
        new(nameof(ConfigurationRecord.Id), $"Id '{id}' is not a well-formed record id.");

    /// <summary>The declared Type is not one of the library's supported value types.</summary>
    public static ConfigurationValidationException UnsupportedType(string declaredType) =>
        new(nameof(ConfigurationRecord.Type),
            $"Type '{declaredType}' is not supported. Supported types: {SupportedTypeNames}.");

    /// <summary>The Value cannot be parsed as the declared Type by the library's reader.</summary>
    public static ConfigurationValidationException ValueTypeMismatch(string value, string declaredType) =>
        new(nameof(ConfigurationRecord.Value),
            $"Value '{value}' cannot be parsed as the declared type '{declaredType}'.");
}
