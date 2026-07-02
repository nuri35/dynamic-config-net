namespace DynamicConfig.Library.Exceptions;

/// <summary>
/// Base type for every exception this library throws on its configuration paths,
/// so consumers can catch one type to mean "dynamic configuration problem".
/// </summary>
public abstract class DynamicConfigurationException : Exception
{
    protected DynamicConfigurationException(string message)
        : base(message)
    {
    }
}
