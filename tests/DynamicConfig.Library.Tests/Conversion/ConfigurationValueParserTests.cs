using DynamicConfig.Library.Conversion;
using DynamicConfig.Library.Models;

namespace DynamicConfig.Library.Tests.Conversion;

public class ConfigurationValueParserTests
{
    [Theory]
    [InlineData("any text")]
    [InlineData("")] // an empty string is still a valid string value
    [InlineData("   ")]
    public void IsParseableAs_String_AcceptsAnyNonNullValue(string rawValue)
    {
        Assert.True(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.String, rawValue));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-7")]
    [InlineData(" 13 ")] // NumberStyles.Integer tolerates surrounding whitespace
    public void IsParseableAs_Int_AcceptsIntegerLiterals(string rawValue)
    {
        Assert.True(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.Int, rawValue));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")] // fractional input is not an int — no silent truncation
    [InlineData("")]
    public void IsParseableAs_Int_RejectsNonIntegerInput(string rawValue)
    {
        Assert.False(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.Int, rawValue));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-0.25")]
    [InlineData("42")] // an integer literal is a valid double
    public void IsParseableAs_Double_AcceptsInvariantCultureFloats(string rawValue)
    {
        Assert.True(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.Double, rawValue));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,5")] // comma decimal separator (tr-TR style) must NOT parse — invariant culture only
    [InlineData("")]
    public void IsParseableAs_Double_RejectsNonInvariantOrCorruptInput(string rawValue)
    {
        Assert.False(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.Double, rawValue));
    }

    [Theory]
    [InlineData("1")] // the case PDF's own sample stores booleans as 1/0
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("False")]
    [InlineData(" true ")]
    public void IsParseableAs_Bool_AcceptsOneZeroAndTrueFalseSpellings(string rawValue)
    {
        Assert.True(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.Bool, rawValue));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("2")]
    [InlineData("")]
    public void IsParseableAs_Bool_RejectsAnythingElse(string rawValue)
    {
        Assert.False(ConfigurationValueParser.IsParseableAs(ConfigurationValueType.Bool, rawValue));
    }

    [Theory]
    [InlineData(ConfigurationValueType.String)]
    [InlineData(ConfigurationValueType.Int)]
    [InlineData(ConfigurationValueType.Double)]
    [InlineData(ConfigurationValueType.Bool)]
    public void IsParseableAs_NullValue_IsNeverParseable(ConfigurationValueType valueType)
    {
        Assert.False(ConfigurationValueParser.IsParseableAs(valueType, null));
    }
}
