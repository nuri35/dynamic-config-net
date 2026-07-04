using DynamicConfig.Library.Exceptions;
using DynamicConfig.Library.Models;
using DynamicConfig.Library.Tests.Fakes;

namespace DynamicConfig.Library.Tests;

/// <summary>
/// Audit Block 1 (Part C) edge pins: conversion corners, unsupported T, key
/// validation, multi-reader independence, interval extremes, empty-boot.
/// Every outcome must be DEFINED — the right custom exception or a correct
/// parse, never a leaked FormatException/OverflowException.
/// </summary>
public class AuditBlock1EdgeTests
{
    private const int LongIntervalMs = 60_000;

    private static ConfigurationRecord Record(string name, string type, string value, string app = "SERVICE-A") =>
        new() { Id = Guid.NewGuid().ToString(), Name = name, Type = type, Value = value, IsActive = true, ApplicationName = app };

    private static ConfigurationReader Reader(params ConfigurationRecord[] records) =>
        new("SERVICE-A", new FakeConfigurationStorageProvider(records), LongIntervalMs);

    // --- 12: conversion corners -------------------------------------------------

    [Fact]
    public void IntOverflowValue_ThrowsValueFormat_NotOverflowException()
    {
        using var reader = Reader(Record("Big", "int", "99999999999999"));
        Assert.Throws<ConfigurationValueFormatException>(() => reader.GetValue<int>("Big"));
    }

    [Theory]
    [InlineData("NaN", double.NaN)]
    [InlineData("Infinity", double.PositiveInfinity)]
    public void DoubleSpecialValues_ParseToTheirIeeeMeaning(string raw, double expected)
    {
        // Defined outcome: NumberStyles.Float accepts IEEE spellings — documented, not accidental.
        using var reader = Reader(Record("D", "double", raw));
        Assert.Equal(expected, reader.GetValue<double>("D"));
    }

    [Fact]
    public void WhitespacePaddedInt_Parses()
    {
        using var reader = Reader(Record("Pad", "int", " 50 "));
        Assert.Equal(50, reader.GetValue<int>("Pad"));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("double")]
    [InlineData("bool")]
    public void EmptyStringValue_NonStringTypes_ThrowValueFormat(string type)
    {
        using var reader = Reader(Record("E", type, ""));
        Assert.Throws<ConfigurationValueFormatException>(() =>
        {
            _ = type switch
            {
                "int" => (object)reader.GetValue<int>("E"),
                "double" => reader.GetValue<double>("E"),
                _ => reader.GetValue<bool>("E"),
            };
        });
    }

    [Fact]
    public void EmptyStringValue_StringType_IsServedAsIs()
    {
        using var reader = Reader(Record("E", "string", ""));
        Assert.Equal(string.Empty, reader.GetValue<string>("E"));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    public void BoolCasings_AllParse(string raw, bool expected)
    {
        using var reader = Reader(Record("B", "bool", raw));
        Assert.Equal(expected, reader.GetValue<bool>("B"));
    }

    // --- 13: unsupported requested T ----------------------------------------------

    [Fact]
    public void UnsupportedRequestedTypes_ThrowTypeMismatch_NotCastCrash()
    {
        using var reader = Reader(Record("K", "int", "5"));
        Assert.Throws<ConfigurationTypeMismatchException>(() => reader.GetValue<decimal>("K"));
        Assert.Throws<ConfigurationTypeMismatchException>(() => reader.GetValue<DateTime>("K"));
        Assert.Throws<ConfigurationTypeMismatchException>(() => reader.GetValue<ConfigurationRecord>("K"));
    }

    // --- 14: key argument validation -------------------------------------------------

    [Fact]
    public void NullEmptyWhitespaceKeys_ThrowArgumentValidation_NotKeyNotFound()
    {
        using var reader = Reader(Record("K", "string", "v"));
        Assert.Throws<ArgumentNullException>(() => reader.GetValue<string>(null!));
        Assert.Throws<ArgumentException>(() => reader.GetValue<string>(""));
        Assert.Throws<ArgumentException>(() => reader.GetValue<string>("   "));
    }

    // --- 15: two readers side by side, no shared state ----------------------------------

    [Fact]
    public async Task TwoReadersInOneProcess_HaveFullyIndependentSnapshots()
    {
        var providerA = new FakeConfigurationStorageProvider(Record("SiteName", "string", "a-value", "SERVICE-A"));
        var providerB = new FakeConfigurationStorageProvider(Record("SiteName", "string", "b-value", "SERVICE-B"));
        using var readerA = new ConfigurationReader("SERVICE-A", providerA, LongIntervalMs);
        using var readerB = new ConfigurationReader("SERVICE-B", providerB, LongIntervalMs);

        Assert.Equal("a-value", readerA.GetValue<string>("SiteName"));
        Assert.Equal("b-value", readerB.GetValue<string>("SiteName"));

        // Mutating B's world and refreshing must not leak into A.
        providerB.SetRecords(Record("SiteName", "string", "b-changed", "SERVICE-B"));
        await readerB.RefreshSnapshotAsync();
        Assert.Equal("a-value", readerA.GetValue<string>("SiteName"));
        Assert.Equal("b-changed", readerB.GetValue<string>("SiteName"));
    }

    // --- 18: interval extremes ------------------------------------------------------------

    [Fact]
    public async Task OneMillisecondInterval_PollsWithoutPileupAndDisposesCleanly()
    {
        var provider = new FakeConfigurationStorageProvider(Record("K", "string", "v"));
        await using (var reader = new ConfigurationReader("SERVICE-A", provider, 1))
        {
            await provider.WaitForCallsAsync(5); // several ticks served one at a time
            Assert.Equal("v", reader.GetValue<string>("K"));
        }
    }

    [Fact]
    public void IntMaxValueInterval_ConstructsWithoutOverflow()
    {
        var provider = new FakeConfigurationStorageProvider(Record("K", "string", "v"));
        using var reader = new ConfigurationReader("SERVICE-A", provider, int.MaxValue);
        Assert.Equal("v", reader.GetValue<string>("K"));
    }

    // --- B11 complement: reachable-but-empty storage is a valid boot ------------------------

    [Fact]
    public void EmptyRecordSet_BootsFine_UnknownKeyThrowsKeyNotFound()
    {
        using var reader = Reader(); // zero records: reachable storage, empty result
        Assert.Throws<ConfigurationKeyNotFoundException>(() => reader.GetValue<string>("Anything"));
    }
}
