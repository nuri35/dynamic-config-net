# DynamicConfig — Dynamic Configuration Library for .NET 8

A reusable .NET 8 class library that replaces static configuration files (`appsettings.json`, `web.config`, `app.config`) with a **live, storage-backed configuration system**. Configuration values are stored in MongoDB, managed through a web UI, and picked up by running services **without any deployment, restart, or recycle**. Each service sees only its own active configuration records, and the library keeps working from its last known-good snapshot even when storage goes down.

<p align="center">
  <img src="docs/images/ui-main.png" alt="Admin UI — configuration records across all applications with status indicators" width="900" />
  <br/><em>Admin UI — all applications, status at a glance</em>
</p>

<p align="center">
  <img src="docs/images/ui-form-validation.png" alt="Create form showing a field-level validation error under the Value input" width="900" />
  <br/><em>Field-level validation via RFC 7807 <code>fieldName</code> — the error lands under the exact input</em>
</p>

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
        PROV["IConfigurationStorageProvider"]
    end

    MONGO[("MongoDB<br/>configuration records")]
    MQ[("RabbitMQ<br/>fanout: config-changed")]
    UI["WebUI (admin) — REST API /api/configurations + Swagger, frontend<br/>DTO shape check → service rules → admin repository<br/>errors as RFC 7807 ProblemDetails"]

    APP -- "lock-free read" --> CR
    CR --> SNAP
    POLL -- "atomic swap" --> SNAP
    POLL --> PROV
    PROV -- "ApplicationName + IsActive<br/>filtered query" --> MONGO
    UI -- "validated writes —<br/>same collection the pollers read" --> MONGO
    UI -. "publish signal<br/>(never fails the write)" .-> MQ
    MQ -. "config-changed { applicationName }<br/>match → instant refresh" .-> POLL
```

> The event path is a **signal, not state** ([ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md), accepted): the broker accelerates propagation to sub-second, polling remains the guaranteed base layer. The consumer half is opt-in per reader via `DYNAMIC_CONFIG_RABBITMQ_URI` — absent, the library is pure polling.

## How It Works

1. **Initialization** — a service constructs the reader with exactly three parameters:
   `new ConfigurationReader(applicationName, connectionString, refreshTimerIntervalInMs)`.
   The reader immediately loads its records and starts a background refresh loop.
2. **Reads are lock-free** — `GetValue<T>(key)` reads from an **immutable in-memory snapshot**. No I/O, no locks, no `await` on the hot path.
3. **Polling refresh** — a `PeriodicTimer`-based background loop re-queries storage at the configured interval, builds a *new* immutable snapshot off to the side, and swaps it in atomically (single reference swap). Readers never observe a half-updated state.
4. **Resilience** — if storage becomes unreachable, the refresh cycle fails *silently for readers*: the last successfully loaded snapshot stays in place and `GetValue<T>` keeps serving it. When storage recovers, the next cycle swaps in fresh data.
   *First-load behavior:* **fail-fast** — if storage is unreachable at startup, the constructor throws. The fallback clause presupposes at least one successful load, a config-less service would misbehave on every read anyway, and a boot-time failure plugs straight into orchestrator restart policies ([ADR 0004](docs/adr/0004-fail-fast-initial-load.md)).
5. **Isolation** — the storage query itself filters by `ApplicationName` **and** `IsActive`. Records belonging to other services never enter a reader's memory, so isolation cannot be bypassed by an in-memory bug.
6. **Write path (admin)** — the WebUI's REST API (`/api/configurations`, browsable via `/swagger`) writes to the **same collection the pollers read**, so a saved change reaches every consumer within one poll interval. Two validation layers with zero overlap: DTO DataAnnotations reject shape garbage at the HTTP door; the admin service enforces semantics — crucially, *Value must be parseable as the declared Type*, checked with the **library's own parser** (single source of truth: what can be written is exactly what readers can parse; collection/database names are likewise shared constants). Every write stamps `LastModifiedDate` (UTC), and all errors leave as RFC 7807 ProblemDetails (400 + `fieldName`, 404 + `recordId`, 500 with no internals leaked) from one central exception handler.
7. **Instant refresh (broker, Phase 5)** — after every successful write the WebUI publishes a thin `config-changed` signal (`{ applicationName, occurredAtUtc }` — never values) to a RabbitMQ fanout exchange; each reader instance consumes on its own exclusive auto-delete queue, drops foreign application names, and on a match early-triggers the same refresh path the poller uses. The consumer is **opt-in via the `DYNAMIC_CONFIG_RABBITMQ_URI` environment variable** (see Usage): without it the reader runs pure polling, byte-identical to the core. Sub-second propagation when the broker is healthy — measured at ~1 s end-to-end against a 60 s poll interval — and if it isn't, **polling still converges within one interval**: the broker is an accelerator, never a dependency ([ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md)).

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

### Why atomic snapshot swap (over locking) — [ADR 0002](docs/adr/0002-atomic-snapshot-swap.md)
- The read path (`GetValue<T>`) is the hot path — it must never block. Snapshots are **immutable dictionaries**; the refresh loop builds a complete new snapshot and publishes it with a single atomic reference swap (`Interlocked.Exchange` / `volatile` read).
- Readers therefore see either the *entire old* config set or the *entire new* one — never a torn, half-updated state. No reader/writer locks, no contention, no deadlock surface.
- This is the same pattern Node.js developers get for free from the single-threaded event loop — in multi-threaded .NET it must be engineered explicitly.

### Why storage sits behind an interface — [ADR 0003](docs/adr/0003-storage-behind-interface.md)
- `IConfigurationStorageProvider` (Strategy/Repository pattern) decouples `ConfigurationReader` from MongoDB. The core reader is unit-tested against a mocked provider — no database required.
- Swapping Mongo for Redis, SQL, or a file provider is a new implementation of one small interface; the reader, conversion engine, and refresh loop are untouched.

### Why polling + broker hybrid — [ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md)
- **Polling** (mandatory, always on) gives *guaranteed consistency*: correct with no extra infrastructure, catches anything a lost message would miss.
- **Broker (RabbitMQ)** adds *low latency*: a fanout `config-changed` event refreshes every subscribed reader within milliseconds of a UI change. Events are signals, not state — MongoDB stays the single source of truth. (Fanout over topic deliberately: config changes are rare, so consumer-side filtering is free; topic-with-routing-key=`applicationName` is the documented at-scale alternative.)
- Each covers the other's weakness: broker down → polling still converges; long poll interval → broker still delivers instant updates.
- The event is deliberately thin (`applicationName` + timestamp, no values): redelivery is harmless (refresh is idempotent), no second source of truth, no sensitive data in flight. A publish failure never fails the write — log-and-continue, polling carries it.

> **Note — no authentication (deliberate):** the case does not require auth, so the admin UI ships without it; in production this surface would sit behind a reverse proxy with SSO or ASP.NET Core Identity/JWT. See [phase-4.md](docs/phases/phase-4.md).

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
// Wrong type for a key   -> ConfigurationTypeMismatchException (strict: no int->double widening)
// Corrupt stored value   -> ConfigurationValueFormatException (fix the record, not the code)
// Inactive record        -> not visible (treated as not found)
// Other service's record -> not visible (filtered at the storage query)
```

### Instant refresh — opt-in via `DYNAMIC_CONFIG_RABBITMQ_URI`

The constructor is frozen by the case at three parameters, so the broker consumer is opted into through **one environment variable**:

```bash
# hybrid mode: broker-triggered refresh in milliseconds + polling as the guaranteed base
DYNAMIC_CONFIG_RABBITMQ_URI=amqp://guest:guest@localhost:5672
```

| State | Behavior |
|---|---|
| Variable set (non-blank) | The reader additionally binds an exclusive auto-delete queue to the `dynamicconfig.config-changed` fanout exchange; a matching event triggers the same refresh the poller runs. Broker unreachable, or the URI malformed? Logged, reader continues polling-only — never a boot failure. |
| Variable absent / blank | No consumer is created at all — pure polling, byte-identical to the core behavior. Absence is a mode, not an error. |

One startup trace line always states which mode the reader is in ("instant-refresh consumer started" / "polling-only mode").

## Running the Project

Prerequisites: Docker Desktop (with the Compose v2 plugin — the `docker compose` subcommand). Nothing else; the .NET SDK is only needed for the dev-mode alternative below.

> **Keep the published ports free — the containers must be the only listeners.** Before starting, make sure nothing else on the host holds `27017` (Mongo), `5672`/`15672` (RabbitMQ), `8080` (Web UI) or `8081` (demo). A pre-existing listener on a port silently shadows the container's mapping — e.g. a native `mongod`/`RabbitMQ` service, or any local web server on `8080` — so requests reach the wrong process instead of producing an obvious error. Stop (and ideally disable) such services first; shadow-port findings are documented in [phase-4.md](docs/phases/phase-4.md), [phase-5.md](docs/phases/phase-5.md) and [phase-6.md](docs/phases/phase-6.md).

### Compose (recommended — one command boots everything)

```bash
docker compose up -d --build
```

That builds and starts all four services in dependency order: `mongo` and `rabbitmq` come up first, and `webui` + `demoservice` wait for both to report healthy before they start.

| Service | URL | Notes |
|---|---|---|
| Web UI (config management) | http://localhost:8080 | list / add / update, client-side name filter; Swagger at `/swagger` |
| Demo service (library consumer) | http://localhost:8081 | live config values for `SERVICE-A` (`GET /` + a console line every 2s) |
| MongoDB | mongodb://localhost:27017 | database `DynamicConfigDb` |
| RabbitMQ | amqp://localhost:5672 · UI http://localhost:15672 (guest/guest) | fanout `dynamicconfig.config-changed`; the demo service runs in hybrid mode (broker + polling) |

**First run starts with an empty database — that is expected.** The Web UI is the seeding tool: open http://localhost:8080, click **Add configuration**, and create a record (e.g. `Name=SiteName`, `Type=string`, `Value=soty.io`, `ApplicationName=SERVICE-A`). The demo service on http://localhost:8081 picks it up within a second via the broker (and within one 30s poll interval regardless). Records live in the `mongo-data` volume and survive `docker compose down`; use `docker compose down -v` to reset to an empty database.

Shut down with `docker compose down` (keep data) or `docker compose down -v` (wipe the Mongo volume).

### Dev mode (alternative — hot-reload the .NET services)

Run the two .NET services on the host against containerized storage. Needs the .NET 8 SDK.

```bash
# 1. Start storage + broker only
docker compose up -d mongo rabbitmq

# 2. Run the web UI and the demo service on the host
dotnet run --project src/DynamicConfig.WebUI
dotnet run --project src/DynamicConfig.DemoService
```

Change a value in the Web UI and watch the demo service pick it up — sub-second via the broker fanout, and within one poll interval even with the broker down ([ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md)).

## Running Tests

```bash
dotnet test
```

The core library is fully unit-tested against a mocked `IConfigurationStorageProvider` (no MongoDB needed). Coverage includes: type conversion for all four supported types, conversion-mismatch errors, key-not-found behavior, `ApplicationName`/`IsActive` isolation, refresh picking up new and changed records, and the storage-down → last-good-snapshot fallback.

## Requirements Coverage

### Mandatory requirements (all covered by CORE phases 0–4)

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

Every row above is verified two ways: the **213-test suite** (`dotnet test` — 131 library + 82 WebUI, zero failures) and a **final evaluator simulation** — clean `docker compose down -v` start, README-only knowledge, the case walked in PDF order, 7/7 PASS ([phase-6.md](docs/phases/phase-6.md)).

### Extra points

Quality practices are embedded in the CORE phases (they are *how the code is written*, not deferrable features); infrastructure extras land in EXTRA phases 5–7.

| Bonus item | Status | Where |
|---|---|---|
| TPL, async/await | ✅ done (embedded in core) | all I/O paths async end-to-end from the first line |
| Concurrency-safe design | ✅ done (embedded in core) | immutable snapshot + atomic reference swap, lock-free reads ([ADR 0002](docs/adr/0002-atomic-snapshot-swap.md)) |
| Design & architectural patterns | ✅ done (embedded in core) | Strategy/Repository (`IConfigurationStorageProvider`), immutable snapshot |
| TDD | ✅ done (embedded in core) | tests land in the same phase as the code they specify; see commit history |
| Unit tests | ✅ done (embedded in core) | 213 tests: `tests/DynamicConfig.Library.Tests` (131, xUnit, mocked storage) + `tests/DynamicConfig.WebUI.Tests` (82) |
| MongoDB/Redis storage | ✅ done | MongoDB (`mongo:7`), [ADR 0001](docs/adr/0001-mongodb-as-storage.md) |
| Runnable project | ✅ done | `docker compose up -d --build` boots all four services (storage-only + `dotnet run` remains the dev-mode alternative) |
| Documentation | ✅ done | this README + [architecture doc](docs/architecture.md) + [ADRs](docs/adr/) + [phase docs](docs/phases/) |
| Source control | ✅ done | GitHub, conventional commits per phase |
| Message broker | ✅ done (Phase 5) | RabbitMQ `config-changed` fanout: WebUI publisher + library consumer ([ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md)) |
| docker-compose for the whole ecosystem | ✅ done (Phase 6) | single command boots mongo + rabbitmq + webui + demoservice ([phase-6.md](docs/phases/phase-6.md)) |

## Repository Structure

```
dynamic-config-net/
├── CLAUDE.md                          # project constitution: decisions, phase table, standards
├── README.md                          # this file
├── docker-compose.yml                 # Phase 0: mongo only → Phase 6: full ecosystem
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

## Deliberate Non-Goals

Scope lines drawn on purpose (surfaced by the library audit), not omissions:

- **`netstandard2.0` multi-targeting** — the case mandates .NET 8; classic .NET Framework/WCF consumers would require multi-targeting plus a MongoDB/RabbitMQ driver downgrade audit.
- **NuGet packaging metadata** (PackageId, version, license) — the deliverable is a project-referenced dll per the case; packaging is a distribution concern outside the brief.
- **Finalizer/`SafeHandle` for an undisposed reader** — an undisposed `ConfigurationReader` keeps polling for the process lifetime, which is exactly its contract; disposal is the host's job (`await using`), as with any `IHostedService`-style resource, and a finalizer is not warranted for managed-only resources.
