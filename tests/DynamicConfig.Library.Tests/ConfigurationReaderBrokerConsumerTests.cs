using DynamicConfig.Library.Messaging;
using DynamicConfig.Library.Models;
using DynamicConfig.Library.Tests.Fakes;

namespace DynamicConfig.Library.Tests;

/// <summary>
/// Phase 5.2 consumer behavior, against the mocked broker seam — no live
/// RabbitMQ. Env-var tests mutate process state, so this class must not run
/// its members in parallel (xunit default: same-class tests are sequential).
/// </summary>
public class ConfigurationReaderBrokerConsumerTests
{
    private const string OwnApplication = "SERVICE-A";
    private const int LongIntervalMs = 60_000; // keep the timer out of the way

    private static ConfigurationRecord ActiveRecord(string name, string value) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Type = "string",
        Value = value,
        IsActive = true,
        ApplicationName = OwnApplication,
    };

    [Fact]
    public async Task MatchingEvent_TriggersExactlyOneRefresh()
    {
        var provider = new FakeConfigurationStorageProvider(ActiveRecord("SiteName", "old"));
        var changeSource = new FakeConfigurationChangeSource();
        await using var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs, changeSource);
        await reader.WaitForConsumerStartAsync();
        var callsAfterBoot = provider.CallCount;
        provider.SetRecords(ActiveRecord("SiteName", "new"));

        await changeSource.DeliverAsync(new ConfigurationChangedEvent(OwnApplication, DateTime.UtcNow));

        Assert.Equal(callsAfterBoot + 1, provider.CallCount); // exactly one broker-triggered fetch
        Assert.Equal("new", reader.GetValue<string>("SiteName")); // and it landed in the snapshot
    }

    [Fact]
    public async Task ForeignApplicationEvent_TriggersNoRefresh()
    {
        var provider = new FakeConfigurationStorageProvider(ActiveRecord("SiteName", "old"));
        var changeSource = new FakeConfigurationChangeSource();
        await using var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs, changeSource);
        await reader.WaitForConsumerStartAsync();
        var callsAfterBoot = provider.CallCount;

        await changeSource.DeliverAsync(new ConfigurationChangedEvent("SERVICE-B", DateTime.UtcNow));

        Assert.Equal(callsAfterBoot, provider.CallCount); // silent drop: no fetch at all
    }

    [Fact]
    public async Task BrokerUnreachableAtStart_ReaderBootsPollingOnly()
    {
        // The ADR 0004 asymmetry: Mongo down at boot -> throw (data source);
        // broker down at boot -> log and continue (accelerator).
        var provider = new FakeConfigurationStorageProvider(ActiveRecord("SiteName", "value"));
        var changeSource = new FakeConfigurationChangeSource
        {
            StartException = new InvalidOperationException("broker unreachable"),
        };

        await using var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs, changeSource);
        await reader.WaitForConsumerStartAsync(); // must complete despite the failure

        Assert.Equal("value", reader.GetValue<string>("SiteName")); // CORE serving normally
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheChangeSource()
    {
        var provider = new FakeConfigurationStorageProvider(ActiveRecord("SiteName", "value"));
        var changeSource = new FakeConfigurationChangeSource();

        var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs, changeSource);
        await reader.WaitForConsumerStartAsync();
        await reader.DisposeAsync();
        await reader.DisposeAsync(); // double-dispose stays safe

        Assert.True(changeSource.Disposed);
    }

    [Fact]
    public async Task NoEnvironmentVariableAndNoSeamSource_NoConsumerExists()
    {
        // Pins CORE: without opt-in the reader is Phase 4, byte-for-byte behavior.
        var originalValue = Environment.GetEnvironmentVariable(
            RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName);
        Environment.SetEnvironmentVariable(RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName, null);
        try
        {
            var provider = new FakeConfigurationStorageProvider(ActiveRecord("SiteName", "value"));

            await using var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs);
            await reader.WaitForConsumerStartAsync();

            Assert.False(reader.IsInstantRefreshConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName, originalValue);
        }
    }

    [Fact]
    public async Task EnvironmentVariablePresentButBrokerDown_ConstructorDoesNotThrow()
    {
        // A URI nothing listens on: the real RabbitMQ source will fail to connect.
        var originalValue = Environment.GetEnvironmentVariable(
            RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName);
        Environment.SetEnvironmentVariable(
            RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName, "amqp://guest:guest@localhost:1");
        try
        {
            var provider = new FakeConfigurationStorageProvider(ActiveRecord("SiteName", "value"));

            await using var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs);
            await reader.WaitForConsumerStartAsync(); // completes despite the dead broker

            Assert.True(reader.IsInstantRefreshConfigured); // opt-in was requested...
            Assert.Equal("value", reader.GetValue<string>("SiteName")); // ...and CORE still serves
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName, originalValue);
        }
    }

    [Fact]
    public async Task ConcurrentBrokerAndManualRefreshes_SnapshotStaysConsistent()
    {
        // Pins the no-serialization decision: RefreshSnapshotAsync is safe to run
        // concurrently (each call builds a complete snapshot; Volatile.Write is
        // last-write-wins on the reference), so broker- and timer-triggered
        // refreshes need no gate. Both keys always come from ONE generation.
        var provider = new FakeConfigurationStorageProvider(
            ActiveRecord("KeyA", "gen0"), ActiveRecord("KeyB", "gen0"));
        var changeSource = new FakeConfigurationChangeSource();
        await using var reader = new ConfigurationReader(OwnApplication, provider, LongIntervalMs, changeSource);
        await reader.WaitForConsumerStartAsync();

        for (var generation = 1; generation <= 20; generation++)
        {
            var value = $"gen{generation}";
            provider.SetRecords(ActiveRecord("KeyA", value), ActiveRecord("KeyB", value));

            var brokerRefresh = changeSource.DeliverAsync(
                new ConfigurationChangedEvent(OwnApplication, DateTime.UtcNow));
            var timerRefresh = reader.RefreshSnapshotAsync();
            await Task.WhenAll(brokerRefresh, timerRefresh);

            Assert.Equal(reader.GetValue<string>("KeyA"), reader.GetValue<string>("KeyB"));
        }
    }
}
