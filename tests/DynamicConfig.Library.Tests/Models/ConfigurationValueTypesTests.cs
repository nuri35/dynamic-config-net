using DynamicConfig.Library.Models;

namespace DynamicConfig.Library.Tests.Models;

public class ConfigurationValueTypesTests
{
    [Theory]
    [InlineData("string", ConfigurationValueType.String)]
    [InlineData("String", ConfigurationValueType.String)]
    [InlineData("int", ConfigurationValueType.Int)]
    [InlineData("Int", ConfigurationValueType.Int)] // exact casing used in the case PDF's sample data
    [InlineData("integer", ConfigurationValueType.Int)] // the case prose says "integer"
    [InlineData("INTEGER", ConfigurationValueType.Int)]
    [InlineData("double", ConfigurationValueType.Double)]
    [InlineData("Double", ConfigurationValueType.Double)]
    [InlineData("bool", ConfigurationValueType.Bool)]
    [InlineData("boolean", ConfigurationValueType.Bool)] // the case prose says "boolean"
    [InlineData(" bool ", ConfigurationValueType.Bool)] // tolerate stray whitespace from manual UI entry
    public void TryParse_KnownTypeName_MapsToExpectedValueType(string storageTypeName, ConfigurationValueType expected)
    {
        var parsed = ConfigurationValueTypes.TryParse(storageTypeName, out var valueType);

        Assert.True(parsed);
        Assert.Equal(expected, valueType);
    }

    [Theory]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_UnknownOrEmptyTypeName_ReturnsFalse(string? storageTypeName)
    {
        var parsed = ConfigurationValueTypes.TryParse(storageTypeName, out _);

        Assert.False(parsed);
    }
}
