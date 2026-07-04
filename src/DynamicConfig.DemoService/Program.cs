using System.Diagnostics;
using DynamicConfig.Library;
using DynamicConfig.Library.Exceptions;

// DemoService — the proof-of-consumption for DynamicConfig.Library. Deliberately
// tiny: it constructs the case's frozen 3-parameter reader, then makes the live
// values observable two ways (a console line every 2 seconds + GET /). The probe
// set is fixed on purpose — each key demonstrates one contract face:
//   SiteName        (string) own active record → served
//   MaxItemCount    (int)    own active record → served; edited live in the demo
//   PromoBanner     (string) does not exist yet → "(absent)" until added via the UI
//   LegacyFlag      (bool)   own but INACTIVE   → "(absent)" (IsActive filtering)
//   IsBasketEnabled (bool)   SERVICE-B's record → "(absent)" (application isolation)

var builder = WebApplication.CreateBuilder(args);

var applicationName = builder.Configuration["DynamicConfig:ApplicationName"] ?? "SERVICE-A";
var connectionString = builder.Configuration.GetConnectionString("Mongo")
    ?? "mongodb://localhost:27017/DynamicConfigDb";
var refreshTimerIntervalInMs = builder.Configuration.GetValue("DynamicConfig:RefreshTimerIntervalInMs", 5000);

// The library reports its refresh mode ("instant-refresh consumer started" /
// "polling-only mode") via System.Diagnostics.Trace so it never imposes a logging
// framework on consumers — but a container has no Trace listener, so those lines
// would vanish from `docker logs`. Bridging Trace to stdout is the HOST's job
// (like choosing a transport for a bare event channel in Node), so do it here,
// before the reader constructs and emits its mode line.
Trace.Listeners.Add(new ConsoleTraceListener());
Trace.AutoFlush = true;

// Fail-fast by design (ADR 0004): if storage is unreachable at boot, this throws
// and the host's restart policy owns the retry. After one successful load, the
// reader survives outages on its last-good snapshot.
await using var configurationReader = new ConfigurationReader(
    applicationName, connectionString, refreshTimerIntervalInMs);

var app = builder.Build();

app.MapGet("/", () => Results.Json(new
{
    applicationName,
    refreshTimerIntervalInMs,
    observedAtUtc = DateTime.UtcNow,
    values = ProbeAll(),
}));

var serverTask = app.RunAsync();

// Observation loop: one line every 2 seconds. GetValue is the lock-free hot path,
// so polling it from here while HTTP requests read concurrently is exactly the
// concurrency model the library promises.
while (!serverTask.IsCompleted)
{
    var probes = ProbeAll().Select(probe => $"{probe.Key}={probe.Value}");
    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {string.Join("  ", probes)}");
    await Task.Delay(TimeSpan.FromSeconds(2));
}

await serverTask;

IReadOnlyDictionary<string, string> ProbeAll() => new Dictionary<string, string>
{
    ["SiteName"] = Describe<string>("SiteName"),
    ["MaxItemCount"] = Describe<int>("MaxItemCount"),
    ["PromoBanner"] = Describe<string>("PromoBanner"),
    ["LegacyFlag"] = Describe<bool>("LegacyFlag"),
    ["IsBasketEnabled"] = Describe<bool>("IsBasketEnabled"),
};

string Describe<T>(string key)
{
    try
    {
        return configurationReader.GetValue<T>(key)?.ToString() ?? "(null)";
    }
    catch (ConfigurationKeyNotFoundException)
    {
        // Expected for inactive, foreign and not-yet-created keys — that absence
        // IS the isolation/IsActive proof this demo exists to show.
        return "(absent)";
    }
}
