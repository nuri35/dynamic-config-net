# CLAUDE.md — Project Constitution

> Living document. Updated at the end of **every** phase. If reality and this file disagree, fix this file in the same commit.

## 1. Project Summary

DynamicConfig is a .NET 8 dynamic configuration system built for a backend developer code case. It replaces static config files (`appsettings.json`, `web.config`) with a MongoDB-backed store so values change **without deployment, restart, or recycle**. A reusable class library (`ConfigurationReader`) serves typed values from an immutable in-memory snapshot refreshed by background polling; a web UI manages the records. Each consuming service sees only its own active records, enforced at the storage query level. Delivery is **CORE-first**: phases 0–4 satisfy every mandatory case requirement; bonus features (RabbitMQ instant refresh, full docker-compose ecosystem) land in EXTRA phases 5–7 only if time allows.

## 2. Locked Decisions

| # | Decision | Rationale (one line) | ADR |
|---|---|---|---|
| 1 | MongoDB as storage (`mongo:7`) | Query-level isolation via `(ApplicationName, IsActive)` compound index; heterogeneous values fit documents; async-native driver | [ADR 0001](docs/adr/0001-mongodb-as-storage.md) |
| 2 | Atomic immutable-snapshot swap for concurrency | Lock-free `GetValue<T>` hot path; readers never see torn state | [ADR 0002](docs/adr/0002-atomic-snapshot-swap.md) |
| 3 | Storage behind `IConfigurationStorageProvider` | Reader unit-testable with mocks; storage swappable without touching core | [ADR 0003](docs/adr/0003-storage-behind-interface.md) |
| 4 | First-load-failure behavior | *To be decided in Phase 3* (throw vs. empty snapshot + background retry; currently leaning empty+retry) | ADR 0004 (pending) |
| 5 | Polling + RabbitMQ hybrid refresh — **EXTRA, Phase 5 only** | Broker = millisecond latency; polling = guaranteed convergence and broker-down fallback. No broker code before Phase 5. | [ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md) (proposed) |

Rules: changing a locked decision requires the user's explicit approval. Any decision made or revised mid-phase updates this table **and** its ADR in the same commit.

## 3. Phase Table

### CORE — mandatory requirements (submittable after Phase 4)

| Phase | Scope | Status | Completed | Outcome | Doc |
|---|---|---|---|---|---|
| 0 | Scaffold, docs workflow, README v1 | done | 2026-07-02 | Solution + 4 projects build clean; mongo-only compose; constitution, ADRs, skills/hook | [phase-0](docs/phases/phase-0.md) |
| 1 | Domain & storage (Mongo provider, compound index, query-level filtering) | done | 2026-07-02 | Record model + provider seam + Mongo provider; isolation proven at query level by rendered-filter test; 30/30 unit tests, no DB dependency | [phase-1](docs/phases/phase-1.md) |
| 2 | Core reader (`GetValue<T>`, conversion engine, TDD, mocked provider) | done | 2026-07-02 | Case-exact public surface + internal DI ctor; FrozenDictionary snapshot; strict conversion engine (invariant culture, bool 1/0, 3 custom exceptions); 59/59 tests, zero DB | [phase-2](docs/phases/phase-2.md) |
| 3 | Refresh & resilience (polling, snapshot swap, fallback, ADR 0004) | pending | — | — | — |
| 4 | Web UI (REST list/add/update + frontend, client-side name filter) | pending | — | — | — |

**✅ CHECKPOINT after Phase 4: every mandatory case requirement is met — project is submittable. EXTRA work needs the user's explicit go-ahead.**

### EXTRA — bonus features (only if time allows, in this order)

| Phase | Scope | Status | Completed | Outcome | Doc |
|---|---|---|---|---|---|
| 5 | RabbitMQ instant refresh (publisher + consumer, graceful degradation, ADR 0005) | pending | — | — | — |
| 6 | Full docker-compose ecosystem (mongo + rabbitmq + webui + demoservice) | pending | — | — | — |
| 7 | Documentation polish (README final pass, diagrams, coverage checklist) | pending | — | — | — |

## 4. Code Standards

- **Meaningful names** — no abbreviations; a method name states what it does, a test name states the behavior it proves.
- **Small, single-responsibility methods**; SOLID throughout (DI and SRP especially).
- **No magic strings/numbers** — constants or enums (`ConfigurationValueType`, collection names).
- **Early returns** over deep nesting.
- **`async/await` end-to-end** on every I/O path from the first line — never retrofitted; no `.Result`, no `.Wait()`, no `async void` outside event handlers (explicit bonus criterion: TPL usage).
- **Custom exceptions with clear messages**: `ConfigurationKeyNotFoundException`, `ConfigurationTypeMismatchException`.
- **Comments explain WHY**, and flag .NET idioms that differ from Node.js expectations (the maintainer's background is NestJS/TypeScript) — e.g. `IHostedService` vs. NestJS lifecycle hooks, `Interlocked.Exchange` vs. single-threaded event loop assumptions.
- The case's public surface is frozen: ctor `ConfigurationReader(applicationName, connectionString, refreshTimerIntervalInMs)` and exactly one public method `T GetValue<T>(string key)`.
- Quality practices are **embedded in CORE phases, not deferred**: TDD lands with the code it specifies; async, concurrency safety, and the provider pattern are designed in from the start.

## 5. Working Rules

- Architecture questions **before** implementing; ask "which module / new or existing / how does this integrate" when structure is ambiguous.
- Show code structure first, then implementation. Production-ready code only.
- **No EXTRA/bonus work inside CORE phases.** RabbitMQ code must not appear before Phase 5. EXTRA phases start only after the user's explicit approval at the Phase 4 checkpoint.
- **End of every phase** (use the `phase-docs` skill): write `docs/phases/phase-N.md`, update the phase table above, write/update ADRs for any architectural decision made during the phase.
- **Before every phase commit**: run the `case-compliance` skill (hard-requirement scan) and the `dotnet-reviewer` skill.
- Conventional commits, one per phase: `feat(phase-1): storage layer with mongo provider`.
- Commits carry the user's identity only — no AI co-author trailers.
- TDD for the core library: tests first, mocked `IConfigurationStorageProvider`, no MongoDB dependency in unit tests.

## 6. Recommended Reading Order

1. **CLAUDE.md** (this file) — constitution + phase table
2. **[docs/architecture.md](docs/architecture.md)** — end-to-end picture
3. **[ADR 0002](docs/adr/0002-atomic-snapshot-swap.md)** — the core concurrency decision (snapshot swap)
4. **docs/phases/phase-3.md** — the most critical phase story (refresh & resilience), once it exists
5. **tests/DynamicConfig.Library.Tests** — behavior specification in executable form

## 7. Local Environment Notes

- .NET SDK 8.0.422 installed at `C:\Program Files\dotnet` but **not on PATH in fresh shells** — prefix commands with `$env:PATH = "C:\Program Files\dotnet;$env:PATH"`.
- NuGet had zero configured sources on this machine; `nuget.org` was added manually (`dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`).
- Case source PDF (Turkish): `C:\Users\Nurie\Downloads\Backend Developer Code Case.pdf`.
