# Architecture

End-to-end picture of the DynamicConfig system. For the reasoning behind each choice, see the ADRs in [`docs/adr/`](adr/). Delivery is CORE-first: everything below is mandatory scope except the explicitly marked **EXTRA (planned)** section.

## System Overview (CORE)

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
    UI["WebUI<br/>REST API + frontend<br/>(list / add / update, name filter)"]

    APP -- "lock-free read" --> CR
    CR --> SNAP
    POLL -- "atomic swap" --> SNAP
    POLL --> PROV
    PROV -- "ApplicationName + IsActive<br/>filtered query" --> MONGO
    UI -- "CRUD" --> MONGO
```

## Components (CORE)

| Component | Project | Responsibility |
|---|---|---|
| `ConfigurationReader` | `DynamicConfig.Library` | Public API: 3-param ctor + `T GetValue<T>(string key)`. Owns the snapshot and the refresh loop. |
| Immutable snapshot | `DynamicConfig.Library` | Frozen dictionary of the service's active records; the *only* thing the read path touches. Doubles as the storage-down fallback. |
| Background poller | `DynamicConfig.Library` | `PeriodicTimer` loop; re-queries storage every `refreshTimerIntervalInMs`, builds a new snapshot, swaps atomically. |
| `IConfigurationStorageProvider` | `DynamicConfig.Library` | Storage abstraction (Strategy/Repository). One method surface for "fetch my active records". |
| `MongoConfigurationStorageProvider` | `DynamicConfig.Library` | Mongo implementation; `(ApplicationName, IsActive)` compound index; the isolation filter lives in the query. |
| WebUI | `DynamicConfig.WebUI` | REST API + minimal frontend: list/add/update records, client-side name filter. |
| DemoService | `DynamicConfig.DemoService` | Proof-of-consumption: boots with the library and exposes its live config values. |

## Data Flows (CORE)

### Read path (hot, lock-free)

`GetValue<T>(key)` → volatile read of the current snapshot reference → dictionary lookup → typed conversion result. No I/O, no locks. Unknown key → `ConfigurationKeyNotFoundException`; declared type ≠ requested `T` → `ConfigurationTypeMismatchException`.

### Refresh path (background)

```mermaid
sequenceDiagram
    participant T as PeriodicTimer loop
    participant P as StorageProvider
    participant M as MongoDB
    participant S as Snapshot ref

    loop every refreshTimerIntervalInMs
        T->>P: GetActiveRecordsAsync(applicationName)
        P->>M: find { ApplicationName, IsActive: true }
        alt storage reachable
            M-->>P: records
            P-->>T: records
            T->>T: build new immutable snapshot
            T->>S: atomic reference swap
        else storage down
            M--xP: error
            T->>T: log + keep current snapshot (fallback)
        end
    end
```

## Failure Modes (CORE)

| Failure | Behavior | Guaranteed by |
|---|---|---|
| MongoDB unreachable at runtime | `GetValue<T>` keeps serving the last successful snapshot; poller retries every interval | Snapshot-as-fallback ([ADR 0002](adr/0002-atomic-snapshot-swap.md)) |
| MongoDB unreachable at startup | Constructor throws (fail-fast); host's restart policy retries the boot | [ADR 0004](adr/0004-fail-fast-initial-load.md) |
| Concurrent read during refresh | Reader sees entire old or entire new snapshot, never a mix | Atomic swap ([ADR 0002](adr/0002-atomic-snapshot-swap.md)) |
| Service reads another service's key | Impossible — records are filtered by `ApplicationName` in the Mongo query; foreign records never enter memory | Query-level isolation ([ADR 0001](adr/0001-mongodb-as-storage.md)) |

## EXTRA (planned — Phase 5, only after the Phase 4 checkpoint)

Broker-triggered instant refresh, designed in [ADR 0005](adr/0005-polling-plus-broker-hybrid.md) (status: proposed). WebUI publishes a `config-changed` event to a RabbitMQ **fanout exchange** on every create/update; each library instance consumes it and triggers an immediate refresh through the same storage-query code path the poller uses. The event carries no data — signal, not state — so lost/duplicate messages are harmless and polling remains the guaranteed convergence path. Until Phase 5 lands, change propagation latency is bounded by one poll interval.

```mermaid
sequenceDiagram
    participant U as WebUI
    participant M as MongoDB
    participant X as RabbitMQ (fanout)
    participant L as Library consumer

    U->>M: insert / update record
    U->>X: publish config-changed
    X-->>L: config-changed
    L->>L: trigger immediate refresh (same code path as poller)
```

Additional failure modes this introduces (all degrade to CORE behavior): RabbitMQ down → polling still converges within one interval; lost/duplicate message → refresh is idempotent and polling backstops.
