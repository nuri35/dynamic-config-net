namespace DynamicConfig.Library.Models;

/// <summary>
/// The value types a configuration record can declare in its <c>Type</c> field.
/// The case supports exactly these four; the record's <c>Value</c> is always stored
/// as a string and converted to the declared type inside the library.
/// </summary>
public enum ConfigurationValueType
{
    /// <summary>Value is served as <see cref="string"/>.</summary>
    String,

    /// <summary>Value is served as <see cref="int"/>.</summary>
    Int,

    /// <summary>Value is served as <see cref="double"/>.</summary>
    Double,

    /// <summary>Value is served as <see cref="bool"/>.</summary>
    Bool
}
