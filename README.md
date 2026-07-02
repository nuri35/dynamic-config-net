# DynamicConfig — Dynamic Configuration Library for .NET 8

A reusable .NET 8 class library that replaces static configuration files (`appsettings.json`, `web.config`, `app.config`) with a **live, storage-backed configuration system**. Configuration values are stored in MongoDB, managed through a web UI, and picked up by running services **without any deployment, restart, or recycle**. Each service sees only its own active configuration records, and the library keeps working from its last known-good snapshot even when storage goes down.

## Architecture

```mermaid
flowchart LR
    subgraph consumers["Consuming services (SERVICE-A, SERVICE-B, ...)"]
        APP["DemoService<br/>(any .NET app)"]
    end

    subgraph lib["DynamicConfig.Library (dll)"]
        CR["ConfigurationReader<br/>GetValue&lt;T&gt;(key)"]
        SNAP[("Immutable snapshot<br/>(in-memory cache)")]
        POLL["Background poller<br/>(PeriodicTimer)"]
        SUB["RabbitMQ consumer<br/>(instant refresh)"]
        PROV["IConfigurationStorageProvider"]
    end

    MONGO[("MongoDB<br/>configuration records")]
    MQ{{"RabbitMQ<br/>config-changed (fanout)"}}
    UI["WebUI<br/>REST API + frontend<br/>(list / add / update / filter)"]

    APP -- "lock-free read" --> CR
    CR --> SNAP
    POLL -- "atomic swap" --> SNAP
    SUB -- "trigger refresh" --> POLL
    POLL --> PROV
    PROV -- "ApplicationName + IsActive<br/>filtered query" --> MONGO
    MQ --> SUB
    UI -- "CRUD" --> MONGO
    UI -- "publish on change" --> MQ
```

## How It Works

1. **Initialization** — a service constructs the reader with exactly three parameters:
   `new ConfigurationReader(applicationName, connectionString, refreshTimerIntervalInMs)`.
   The reader immediately loads its records and starts a background refresh loop.
2. **Reads are lock-free** — `GetValue<T>(key)` reads from an **immutable in-memory snapshot**. No I/O, no locks, no `await` on the hot path.
3. **Polling refresh** — a `PeriodicTimer`-based background loop re-queries storage at the configured interval, builds a *new* immutable snapshot off to the side, and swaps it in atomically (single reference swap). Readers never observe a half-updated state.
4. **Broker-triggered refresh (bonus)** — when a record is created or updated in the WebUI, it publishes a `config-changed` event to a RabbitMQ fanout exchange. Each library instance consumes the event and refreshes immediately, so changes propagate in milliseconds instead of waiting for the next poll tick.
5. **Resilience** — if storage becomes unreachable, the refresh cycle fails *silently for readers*: the last successfully loaded snapshot stays in place and `GetValue<T>` keeps serving it. When storage recovers, the next cycle swaps in fresh data. If RabbitMQ is down, event-driven refresh degrades gracefully and polling remains the guaranteed path.
   *First-load behavior:* if storage is unreachable at startup, the reader starts with an empty snapshot and keeps retrying in the background — a degraded-but-alive service beats one that crashes at boot.
6. **Isolation** — the storage query itself filters by `ApplicationName` **and** `IsActive`. Records belonging to other services never enter a reader's memory, so isolation cannot be bypassed by an in-memory bug.

### Record schema

| Field | Type | Example |
|---|---|---|
| Id | ObjectId / string | `665f...` |
| Name | string | `SiteName` |
| Type | string (`string` \| `int` \| `double` \| `bool`) | `string` |
| Value | string (converted by the library) | `soty.io` |
| IsActive | bool | `true` |
| ApplicationName | string | `SERVICE-A` |

## Design Decisions

Every architectural decision is captured as an ADR in [`docs/adr/`](docs/adr/); the highlights:

### Why MongoDB (over Redis / MsSQL / file) — [ADR 0001](docs/adr/0001-mongodb-as-storage.md)
- **Query-level isolation:** a compound index on `(ApplicationName, IsActive)` makes the per-service filtered read the cheapest possible operation, and the filter lives in the storage query — not in application memory.
- **Heterogeneous values:** config records are schemaless by nature (`Value` is a string carrying an int, double, bool...). A document store fits this without column gymnastics.
- **No relational needs:** a single collection, no joins, no transactions — an RDBMS would add ceremony without benefit. Redis would work as a cache but offers weaker querying and no natural durable system-of-record semantics for a management UI.
- **Async-native driver:** the official MongoDB C# driver is async end-to-end, matching the library's fully asynchronous I/O paths (TPL / `async/await` bonus criterion).

### Why polling + message broker hybrid — [ADR 0003](docs/adr/0003-polling-plus-broker-hybrid.md)
- **Broker (RabbitMQ)** gives *low latency*: a fanout `config-changed` event refreshes every subscribed reader within milliseconds of a UI change.
- **Polling** gives *guaranteed consistency*: it needs no extra infrastructure to be correct, catches anything a lost/undelivered message would miss, and is the case's required baseline mechanism.
- Each covers the other's weakness: broker down → polling still converges; long poll interval → broker still delivers instant updates.

### Why atomic snapshot swap (over locking) — [ADR 0002](docs/adr/0002-atomic-snapshot-swap.md)
- The read path (`GetValue<T>`) is the hot path — it must never block. Snapshots are **immutable dictionaries**; the refresh loop builds a complete new snapshot and publishes it with a single atomic reference swap (`Interlocked.Exchange` / `volatile` read).
- Readers therefore see either the *entire old* config set or the *entire new* one — never a torn, half-updated state. No reader/writer locks, no contention, no deadlock surface.
- This is the same pattern Node.js developers get for free from the single-threaded event loop — in multi-threaded .NET it must be engineered explicitly.

### Why storage sits behind an interface — [ADR 0004](docs/adr/0004-storage-behind-interface.md)
- `IConfigurationStorageProvider` (Strategy/Repository pattern) decouples `ConfigurationReader` from MongoDB. The core reader is unit-tested against a mocked provider — no database required.
- Swapping Mongo for Redis, SQL, or a file provider is a new implementation of one small interface; the reader, conversion engine, and refresh loop are untouched.

## Usage

```csharp
using DynamicConfig.Library;

// Exactly three parameters, as required by the case.
var reader = new ConfigurationReader(
    applicationName: "SERVICE-A",
    connectionString: "mongodb://localhost:27017",
    refreshTimerIntervalInMs: 5000);

// Typed reads — conversion happens inside the library.
string siteName   = reader.GetValue<string>("SiteName");     // "soty.io"
bool   basketOn   = reader.GetValue<bool>("IsBasketEnabled");
int    maxItems   = reader.GetValue<int>("MaxItemCount");
double rate       = reader.GetValue<double>("ConversionRate");

// Unknown key            -> ConfigurationKeyNotFoundException
// Wrong type for a key   -> ConfigurationTypeMismatchException
// Inactive record        -> not visible (treated as not found)
// Other service's record -> not visible (filtered at the storage query)
```

## Running the Project

Prerequisites: Docker + Docker Compose.

```bash
docker-compose up --build
```

| Service | URL | Notes |
|---|---|---|
| Web UI (config management) | http://localhost:8080 | list / add / update, client-side name filter |
| Demo service (library consumer) | http://localhost:8081 | shows live config values for `SERVICE-A` |
| RabbitMQ management | http://localhost:15672 | `guest` / `guest` |
| MongoDB | mongodb://localhost:27017 | database `dynamicconfig` |

Change a value in the Web UI and watch the demo service pick it up — instantly via RabbitMQ, or within one poll interval if the broker is unavailable.

## Running Tests

```bash
dotnet test
```

The core library is fully unit-tested against a mocked `IConfigurationStorageProvider` (no MongoDB needed). Coverage includes: type conversion for all four supported types, conversion-mismatch errors, key-not-found behavior, `ApplicationName`/`IsActive` isolation, refresh picking up new and changed records, and the storage-down → last-good-snapshot fallback.

## Requirements Coverage

### Core requirements

| Requirement (case) | Where |
|---|---|
| .NET 8 class library (dll) usable by any project type | `src/DynamicConfig.Library` |
| Record schema `Id, Name, Type, Value, IsActive, ApplicationName` | `ConfigurationRecord` model |
| Init with exactly 3 params (`applicationName, connectionString, refreshTimerIntervalInMs`) | `ConfigurationReader` constructor |
| Single public method `T GetValue<T>(string key)` | `ConfigurationReader.GetValue<T>` |
| Type handling inside the library (`string`, `int`, `double`, `bool`) | conversion engine in the library |
| Only `IsActive = 1` records returned | storage-level query filter |
| Each service sees only its own records | `ApplicationName` filter at storage query level + compound index |
| Periodic check for new records and value changes | `PeriodicTimer` background refresh loop |
| Works from last successful config when storage is unreachable | immutable snapshot kept on refresh failure |
| Web UI: list, add, update records | `src/DynamicConfig.WebUI` |
| Client-side filtering by Name | WebUI frontend |

### Extra points

| Bonus item | Where |
|---|---|
| Message broker | RabbitMQ `config-changed` fanout: WebUI publisher + library consumer |
| TPL, async/await | all I/O paths async end-to-end |
| Concurrency-safe design | immutable snapshot + atomic reference swap, lock-free reads |
| Design & architectural patterns | Strategy/Repository (`IConfigurationStorageProvider`), immutable snapshot, publisher/subscriber |
| TDD | library developed test-first; see commit history |
| Unit tests | `tests/DynamicConfig.Library.Tests` (xUnit, mocked storage) |
| MongoDB/Redis storage | MongoDB (`mongo:7`) |
| Runnable project | `docker-compose up --build` boots the entire ecosystem |
| Documentation | this README + [architecture doc](docs/architecture.md) + [ADRs](docs/adr/) + [phase docs](docs/phases/) |
| Source control | GitHub, conventional commits per phase |
| docker-compose for the whole ecosystem | `docker-compose.yml` (mongo, rabbitmq, webui, demoservice) |

## Repository Structure

```
dynamic-config-net/
├── CLAUDE.md                          # project constitution: decisions, phase table, standards
├── README.md                          # this file
├── docker-compose.yml                 # mongo + rabbitmq + webui + demoservice
├── .claude/                           # AI workflow: review/compliance skills + build-test hook
├── docs/
│   ├── architecture.md                # end-to-end system picture (diagrams, flows, failure modes)
│   ├── adr/                           # architecture decision records (0001–000N)
│   └── phases/                        # one doc per completed development phase
├── src/
│   ├── DynamicConfig.Library/         # the deliverable dll: ConfigurationReader, providers, models
│   ├── DynamicConfig.WebUI/           # ASP.NET Core: REST API + frontend (list/add/update, name filter)
│   └── DynamicConfig.DemoService/     # sample service consuming the library
└── tests/
    └── DynamicConfig.Library.Tests/   # xUnit unit tests (mocked storage provider)
```

## Development Workflow

Development was AI-assisted using a structured phase/ADR workflow I designed: work proceeds in reviewed phases (each documented in [`docs/phases/`](docs/phases/)), every architectural decision is recorded as an ADR in [`docs/adr/`](docs/adr/), and the project constitution ([`CLAUDE.md`](CLAUDE.md)) plus custom compliance/review skills in [`.claude/`](.claude/) — including an automated build+test hook on every code edit — keep the process verifiable. The `.claude/` directory is committed intentionally to make that workflow inspectable.
