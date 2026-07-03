using System.ComponentModel.DataAnnotations;

namespace DynamicConfig.WebUI.Contracts;

/// <summary>
/// Fails validation for whitespace-only strings. Complements <see cref="RequiredAttribute"/>
/// (which covers null and empty): together they give shape-level "required, non-blank"
/// at the HTTP door with an automatic 400 + field detail. Deep rules (supported Type,
/// Value parseable as Type) deliberately stay in the service layer — two validation
/// layers, zero overlap.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBlankAttribute : ValidationAttribute
{
    public NotBlankAttribute()
        : base("The {0} field must not be blank.")
    {
    }

    // null and "" are RequiredAttribute's job — reporting them here too would produce
    // duplicate errors for the same field.
    public override bool IsValid(object? value) =>
        value is not string text || text.Length == 0 || !string.IsNullOrWhiteSpace(text);
}
