using DynamicConfig.Library.Models;
using DynamicConfig.Library.Tests.Fakes;

namespace DynamicConfig.Library.Tests;

public class ConfigurationReaderConstructionTests
{
    private const string ApplicationName = "SERVICE-A";
    private const int AnyRefreshIntervalInMs = 5000;

    // A syntactically valid connection string; validation-failure tests must throw
    // BEFORE any network I/O, so no MongoDB ever runs during this suite.
    private const string AnyConnectionString = "mongodb://localhost:27017";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PublicConstructor_BlankApplicationName_ThrowsArgumentException(string applicationName)
    {
        Assert.Throws<ArgumentException>(
            () => new ConfigurationReader(applicationName, AnyConnectionString, AnyRefreshIntervalInMs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PublicConstructor_BlankConnectionString_ThrowsArgumentException(string connectionString)
    {
        Assert.Throws<ArgumentException>(
            () => new ConfigurationReader(ApplicationName, connectionString, AnyRefreshIntervalInMs));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PublicConstructor_NonPositiveRefreshInterval_ThrowsArgumentOutOfRangeException(int refreshTimerIntervalInMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigurationReader(ApplicationName, AnyConnectionString, refreshTimerIntervalInMs));
    }

    [Fact]
    public void InternalConstructor_NullProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ConfigurationReader(ApplicationName, storageProvider: null!, AnyRefreshIntervalInMs));
    }

    [Fact]
    public void Constructor_PerformsInitialLoad_AskingStorageForExactlyThisApplication()
    {
        var provider = new FakeConfigurationStorageProvider();

        _ = new ConfigurationReader(ApplicationName, provider, AnyRefreshIntervalInMs);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(ApplicationName, provider.LastRequestedApplicationName);
    }

    [Fact]
    public void Constructor_StoresRefreshIntervalForThePollingPhase()
    {
        var reader = new ConfigurationReader(
            ApplicationName, new FakeConfigurationStorageProvider(), refreshTimerIntervalInMs: 1234);

        Assert.Equal(1234, reader.RefreshTimerIntervalInMs);
    }

    [Fact]
    public void Constructor_InitialLoadFails_PropagatesTheStorageException()
    {
        // Fail-fast on initial load, decided in ADR 0004 (Accepted): the case's fallback
        // clause presupposes at least one successful load, and a service booted with zero
        // config would misbehave on every GetValue anyway. This test pins that contract.
        var storageFailure = new InvalidOperationException("storage unreachable");
        var provider = FakeConfigurationStorageProvider.Throwing(storageFailure);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new ConfigurationReader(ApplicationName, provider, AnyRefreshIntervalInMs));

        Assert.Same(storageFailure, thrown);
    }
}
