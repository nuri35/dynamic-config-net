# Architecture

End-to-end picture of the DynamicConfig system. For the reasoning behind each choice, see the ADRs in [`docs/adr/`](adr/).

## System Overview

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
    UI["WebUI<br/>REST API + frontend"]

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

## Components

| Component | Project | Responsibility |
|---|---|---|
| `ConfigurationReader` | `DynamicConfig.Library` | Public API: 3-param ctor + `T GetValue<T>(string key)`. Owns the snapshot and the refresh loop. |
| Immutable snapshot | `DynamicConfig.Library` | Frozen dictionary of the service's active records; the *only* thing the read path touches. Doubles as the storage-down fallback. |
| Background poller | `DynamicConfig.Library` | `PeriodicTimer` loop; re-queries storage each interval, builds a new snapshot, swaps atomically. |
| RabbitMQ consumer | `DynamicConfig.Library` | Subscribes to the `config-changed` fanout exchange; triggers an immediate refresh (bonus path). |
| `IConfigurationStorageProvider` | `DynamicConfig.Library` | Storage abstraction (Strategy/Repository). One method surface for "fetch my active records". |
| `MongoConfigurationStorageProvider` | `DynamicConfig.Library` | Mongo implementation; `(ApplicationName, IsActive)` compound index; the isolation filter lives in the query. |
| WebUI | `DynamicConfig.WebUI` | REST API + minimal frontend: list/add/update records, client-side name filter; publishes `config-changed` on writes. |
| DemoService | `DynamicConfig.DemoService` | Proof-of-consumption: boots with the library and exposes its live config values. |

## Data Flows

### Read path (hot, lock-free)

`GetValue<T>(key)` → volatile read of the current snapshot reference → dictionary lookup → typed conversion result. No I/O, no locks, no allocation beyond the conversion. Unknown key → `ConfigurationKeyNotFoundException`; declared type ≠ requested `T` → `ConfigurationTypeMismatchException`.

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

### Event path (bonus, low latency)

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

The event only *schedules* a refresh — the data always comes from MongoDB. Messages are signals, not state, so a lost message costs at most one poll interval of staleness.

## Failure Modes

| Failure | Behavior | Guaranteed by |
|---|---|---|
| MongoDB unreachable at runtime | `GetValue<T>` keeps serving the last successful snapshot; poller retries every interval | Snapshot-as-fallback (ADR 0002) |
| MongoDB unreachable at startup | See ADR 0005 (Phase 3 decision) — current lean: empty snapshot + background retry | ADR 0005 (pending) |
| RabbitMQ down | Event refresh degrades away silently; polling still converges within one interval | Hybrid refresh (ADR 0003) |
| Lost/duplicate broker message | Harmless — refresh is idempotent and polling backstops losses | Signal-not-state design (ADR 0003) |
| Concurrent read during refresh | Reader sees entire old or entire new snapshot, never a mix | Atomic swap (ADR 0002) |
| Service reads another service's key | Impossible — records are filtered by `ApplicationName` in the Mongo query; foreign records never enter memory | Query-level isolation (ADR 0001) |
