using System.Globalization;
using DynamicConfig.Library.Exceptions;
using DynamicConfig.Library.Models;
using DynamicConfig.Library.Tests.Fakes;

namespace DynamicConfig.Library.Tests;

public class ConfigurationReaderGetValueTests
{
    private const string ApplicationName = "SERVICE-A";

    [Fact]
    public void GetValue_StringRecord_ReturnsStringValue()
    {
        var reader = CreateReader(Record("SiteName", "string", "soty.io"));

        Assert.Equal("soty.io", reader.GetValue<string>("SiteName"));
    }

    [Fact]
    public void GetValue_IntRecord_ReturnsConvertedInt()
    {
        var reader = CreateReader(Record("MaxItemCount", "int", "50"));

        Assert.Equal(50, reader.GetValue<int>("MaxItemCount"));
    }

    [Fact]
    public void GetValue_DoubleRecord_ReturnsConvertedDouble()
    {
        var reader = CreateReader(Record("ConversionRate", "double", "1.5"));

        Assert.Equal(1.5, reader.GetValue<double>("ConversionRate"));
    }

    [Fact]
    public void GetValue_Double_ParsesWithInvariantCulture_EvenUnderTurkishLocale()
    {
        // tr-TR uses ',' as decimal separator; parsing "1.5" with the machine locale
        // would throw or silently mis-parse. The engine must pin InvariantCulture.
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var reader = CreateReader(Record("ConversionRate", "double", "1.5"));

            Assert.Equal(1.5, reader.GetValue<double>("ConversionRate"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData("1", true)] // the case PDF's own sample stores IsBasketEnabled as 1
    [InlineData("0", false)]
    public void GetValue_BoolRecord_AcceptsBothBooleanWordsAndBinaryDigits(string storedValue, bool expected)
    {
        var reader = CreateReader(Record("IsBasketEnabled", "bool", storedValue));

        Assert.Equal(expected, reader.GetValue<bool>("IsBasketEnabled"));
    }

    [Fact]
    public void GetValue_UnknownKey_ThrowsKeyNotFoundWithKeyAndApplicationInMessage()
    {
        var reader = CreateReader(Record("SiteName", "string", "soty.io"));

        var exception = Assert.Throws<ConfigurationKeyNotFoundException>(
            () => reader.GetValue<string>("NoSuchKey"));

        Assert.Contains("NoSuchKey", exception.Message);
        Assert.Contains(ApplicationName, exception.Message);
    }

    [Fact]
    public void GetValue_KeyLookup_IsCaseInsensitive()
    {
        // Matches .NET's own IConfiguration semantics and forgives hand-entered records.
        var reader = CreateReader(Record("SiteName", "string", "soty.io"));

        Assert.Equal("soty.io", reader.GetValue<string>("sitename"));
    }

    [Fact]
    public void GetValue_DuplicateActiveNames_LatestLastModifiedDateWins()
    {
        // A double entry in the management UI must not crash every consumer.
        var reader = CreateReader(
            Record("SiteName", "string", "old.example", modifiedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Record("SiteName", "string", "new.example", modifiedUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal("new.example", reader.GetValue<string>("SiteName"));
    }

    [Fact]
    public void GetValue_RequestedTypeDiffersFromDeclaredType_ThrowsTypeMismatchWithFullContext()
    {
        var reader = CreateReader(Record("MaxItemCount", "int", "50"));

        var exception = Assert.Throws<ConfigurationTypeMismatchException>(
            () => reader.GetValue<bool>("MaxItemCount"));

        Assert.Contains("MaxItemCount", exception.Message);
        Assert.Contains("int", exception.Message);
        Assert.Contains("Boolean", exception.Message);
    }

    [Fact]
    public void GetValue_DoubleRequestedOnIntRecord_ThrowsTypeMismatch_NoImplicitWidening()
    {
        // Strict policy: the declared Type is the record's contract. If a value is meant
        // to be fractional, it must be authored as double — widening would hide mistakes.
        var reader = CreateReader(Record("MaxItemCount", "int", "50"));

        Assert.Throws<ConfigurationTypeMismatchException>(
            () => reader.GetValue<double>("MaxItemCount"));
    }

    [Fact]
    public void GetValue_CorruptValueForDeclaredType_ThrowsValueFormatWithFullContext()
    {
        var reader = CreateReader(Record("MaxItemCount", "int", "abc"));

        var exception = Assert.Throws<ConfigurationValueFormatException>(
            () => reader.GetValue<int>("MaxItemCount"));

        Assert.Contains("MaxItemCount", exception.Message);
        Assert.Contains("int", exception.Message);
        Assert.Contains("abc", exception.Message);
    }

    [Fact]
    public void GetValue_UnsupportedDeclaredType_ThrowsTypeMismatch()
    {
        var reader = CreateReader(Record("LaunchDate", "date", "2026-07-02"));

        var exception = Assert.Throws<ConfigurationTypeMismatchException>(
            () => reader.GetValue<string>("LaunchDate"));

        Assert.Contains("date", exception.Message);
    }

    [Fact]
    public void GetValue_UnsupportedRequestedType_ThrowsTypeMismatch()
    {
        var reader = CreateReader(Record("SiteName", "string", "soty.io"));

        var exception = Assert.Throws<ConfigurationTypeMismatchException>(
            () => reader.GetValue<DateTime>("SiteName"));

        Assert.Contains("DateTime", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetValue_BlankKey_ThrowsArgumentException(string key)
    {
        var reader = CreateReader(Record("SiteName", "string", "soty.io"));

        Assert.Throws<ArgumentException>(() => reader.GetValue<string>(key));
    }

    private static ConfigurationReader CreateReader(params ConfigurationRecord[] records) =>
        new(ApplicationName, new FakeConfigurationStorageProvider(records), refreshTimerIntervalInMs: 5000);

    private static ConfigurationRecord Record(
        string name, string type, string value, DateTime? modifiedUtc = null) => new()
    {
        Id = "665f000000000000000000aa",
        Name = name,
        Type = type,
        Value = value,
        IsActive = true,
        ApplicationName = ApplicationName,
        LastModifiedDate = modifiedUtc ?? new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
