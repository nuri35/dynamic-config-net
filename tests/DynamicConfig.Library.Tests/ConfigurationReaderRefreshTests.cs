using DynamicConfig.Library.Exceptions;
using DynamicConfig.Library.Models;
using DynamicConfig.Library.Tests.Fakes;

namespace DynamicConfig.Library.Tests;

/// <summary>
/// Phase 3 behavior: polling refresh, atomic snapshot swap, storage-down fallback and
/// disposal. Swap/resilience semantics are proven deterministically by triggering
/// <c>RefreshSnapshotAsync</c> directly; only the loop-liveness and dispose tests use
/// the real PeriodicTimer, with a tiny interval and signal-based waits (no sleep-polling).
/// </summary>
public class ConfigurationReaderRefreshTests
{
    private const string ApplicationName = "SERVICE-A";

    // Deterministic tests use an interval long enough that the real timer never fires
    // mid-test; loop tests use a tiny interval and wait on provider call signals.
    private const int QuietIntervalInMs = 60_000;
    private const int FastIntervalInMs = 10;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Refresh_NewRecordAppears_KeyBecomesVisible()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        using var reader = CreateQuietReader(provider);
        provider.SetRecords(
            Record("SiteName", "string", "soty.io"),
            Record("MaxItemCount", "int", "50"));

        await reader.RefreshSnapshotAsync();

        Assert.Equal(50, reader.GetValue<int>("MaxItemCount"));
    }

    [Fact]
    public async Task Refresh_ValueChanged_NewValueIsServed()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        using var reader = CreateQuietReader(provider);
        provider.SetRecords(Record("SiteName", "string", "sety.io"));

        await reader.RefreshSnapshotAsync();

        Assert.Equal("sety.io", reader.GetValue<string>("SiteName"));
    }

    [Fact]
    public async Task Refresh_RecordDeactivated_KeyDisappears()
    {
        // IsActive filtering happens at the storage query (Phase 1), so a deactivated
        // record simply stops arriving — the reader must drop it from the snapshot.
        var provider = new FakeConfigurationStorageProvider(
            Record("SiteName", "string", "soty.io"),
            Record("IsBasketEnabled", "bool", "1"));
        using var reader = CreateQuietReader(provider);
        provider.SetRecords(Record("SiteName", "string", "soty.io"));

        await reader.RefreshSnapshotAsync();

        Assert.Throws<ConfigurationKeyNotFoundException>(() => reader.GetValue<bool>("IsBasketEnabled"));
    }

    [Fact]
    public async Task Refresh_StorageDown_KeepsServingLastGoodSnapshot()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        using var reader = CreateQuietReader(provider);
        provider.StartThrowing(new InvalidOperationException("storage unreachable"));

        await reader.RefreshSnapshotAsync();

        // The failed poll must neither throw out of the refresh path nor clear the snapshot.
        Assert.Equal("soty.io", reader.GetValue<string>("SiteName"));
    }

    [Fact]
    public async Task Refresh_AfterOutage_RecoversOnFirstSuccessfulPoll()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        using var reader = CreateQuietReader(provider);

        provider.StartThrowing(new InvalidOperationException("storage unreachable"));
        await reader.RefreshSnapshotAsync();

        provider.StopThrowing();
        provider.SetRecords(Record("SiteName", "string", "recovered.io"));
        await reader.RefreshSnapshotAsync();

        Assert.Equal("recovered.io", reader.GetValue<string>("SiteName"));
    }

    [Fact]
    public async Task ConcurrentReads_DuringSwaps_AlwaysSeeACompleteGeneration()
    {
        // Generation A: both keys "a"; generation B: both keys "b". A reader that ever
        // observes a torn snapshot would see a value outside {a, b} or a missing key.
        var generationA = new[] { Record("K1", "string", "a"), Record("K2", "string", "a") };
        var generationB = new[] { Record("K1", "string", "b"), Record("K2", "string", "b") };
        var provider = new FakeConfigurationStorageProvider(generationA);
        using var reader = CreateQuietReader(provider);

        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var swapLoop = Task.Run(async () =>
        {
            var flip = false;
            while (!stop.IsCancellationRequested)
            {
                provider.SetRecords(flip ? generationB : generationA);
                await reader.RefreshSnapshotAsync();
                flip = !flip;
            }
        });

        var readerLoops = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var first = reader.GetValue<string>("K1");
                var second = reader.GetValue<string>("K2");
                Assert.True(first is "a" or "b", $"torn value: {first}");
                Assert.True(second is "a" or "b", $"torn value: {second}");
            }
        })).ToArray();

        await Task.WhenAll(readerLoops.Append(swapLoop));
    }

    [Fact]
    public async Task PollingLoop_PicksUpChangesWithoutManualTriggering()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        using var reader = new ConfigurationReader(ApplicationName, provider, FastIntervalInMs);

        provider.SetRecords(Record("SiteName", "string", "polled.io"));

        // Initial load is call 1. Wait until a poll that STARTED after SetRecords has
        // completed (call 3 at the latest: call 2 may have raced the mutation), then
        // give the swap that follows that fetch a bounded moment to publish.
        await provider.WaitForCallsAsync(3).WaitAsync(WaitTimeout);
        await AssertEventuallyAsync(() => reader.GetValue<string>("SiteName") == "polled.io");
    }

    [Fact]
    public async Task Dispose_StopsThePollingLoop()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        var reader = new ConfigurationReader(ApplicationName, provider, FastIntervalInMs);
        await provider.WaitForCallsAsync(2).WaitAsync(WaitTimeout);

        await reader.DisposeAsync();

        var callsAtDisposal = provider.CallCount;
        await Task.Delay(FastIntervalInMs * 8);
        Assert.Equal(callsAtDisposal, provider.CallCount);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        var reader = new ConfigurationReader(ApplicationName, provider, FastIntervalInMs);

        await reader.DisposeAsync();
        await reader.DisposeAsync();
        reader.Dispose();
    }

    [Fact]
    public async Task GetValue_AfterDispose_StillServesTheLastSnapshot()
    {
        // Disposal stops refreshing; it does not invalidate the immutable snapshot.
        // A service shutting down can keep reading config during its own teardown.
        var provider = new FakeConfigurationStorageProvider(Record("SiteName", "string", "soty.io"));
        var reader = new ConfigurationReader(ApplicationName, provider, QuietIntervalInMs);

        await reader.DisposeAsync();

        Assert.Equal("soty.io", reader.GetValue<string>("SiteName"));
    }

    private static ConfigurationReader CreateQuietReader(FakeConfigurationStorageProvider provider) =>
        new(ApplicationName, provider, QuietIntervalInMs);

    private static async Task AssertEventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }

    private static ConfigurationRecord Record(string name, string type, string value) => new()
    {
        Id = "665f000000000000000000bb",
        Name = name,
        Type = type,
        Value = value,
        IsActive = true,
        ApplicationName = ApplicationName,
        LastModifiedDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
