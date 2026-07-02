# CLAUDE.md — Project Constitution

> Living document. Updated at the end of **every** phase. If reality and this file disagree, fix this file in the same commit.

## 1. Project Summary

DynamicConfig is a .NET 8 dynamic configuration system built for a backend developer code case. It replaces static config files (`appsettings.json`, `web.config`) with a MongoDB-backed store so values change **without deployment, restart, or recycle**. A reusable class library (`ConfigurationReader`) serves typed values from an immutable in-memory snapshot refreshed by background polling plus RabbitMQ change events; a web UI manages the records. Each consuming service sees only its own active records, enforced at the storage query level.

## 2. Locked Decisions

| # | Decision | Rationale (one line) | ADR |
|---|---|---|---|
| 1 | MongoDB as storage (`mongo:7`) | Query-level isolation via `(ApplicationName, IsActive)` compound index; heterogeneous values fit documents; async-native driver | [ADR 0001](docs/adr/0001-mongodb-as-storage.md) |
| 2 | Atomic immutable-snapshot swap for concurrency | Lock-free `GetValue<T>` hot path; readers never see torn state | [ADR 0002](docs/adr/0002-atomic-snapshot-swap.md) |
| 3 | Polling + RabbitMQ hybrid refresh | Broker = millisecond latency; polling = guaranteed convergence and broker-down fallback | [ADR 0003](docs/adr/0003-polling-plus-broker-hybrid.md) |
| 4 | Storage behind `IConfigurationStorageProvider` | Reader unit-testable with mocks; storage swappable without touching core | [ADR 0004](docs/adr/0004-storage-behind-interface.md) |
| 5 | First-load-failure behavior | *To be decided in Phase 3* (throw vs. empty snapshot + background retry; currently leaning empty+retry) | ADR 0005 (pending) |

Rules: changing a locked decision requires the user's explicit approval. Any decision made or revised mid-phase updates this table **and** its ADR in the same commit.

## 3. Phase Table

| Phase | Scope | Status | Completed | Outcome | Doc |
|---|---|---|---|---|---|
| 0 | Scaffold, docs workflow, README | done | 2026-07-02 | Solution + 4 projects build clean; compose skeleton; constitution, ADRs 0001–0004, skills/hook | [phase-0](docs/phases/phase-0.md) |
| 1 | Domain & storage (Mongo provider, compound index) | pending | — | — | — |
| 2 | Core reader (`GetValue<T>`, conversion engine, TDD) | pending | — | — | — |
| 3 | Refresh & resilience (polling, snapshot swap, fallback) | pending | — | — | — |
| 4 | RabbitMQ instant refresh (bonus) | pending | — | — | — |
| 5 | Web UI (REST + frontend, name filter) | pending | — | — | — |
| 6 | Packaging (full compose, README polish, e2e) | pending | — | — | — |

## 4. Code Standards

- **Meaningful names** — no abbreviations; a method name states what it does, a test name states the behavior it proves.
- **Small, single-responsibility methods**; SOLID throughout (DI and SRP especially).
- **No magic strings/numbers** — constants or enums (`ConfigurationValueType`, collection names, exchange names).
- **Early returns** over deep nesting.
- **`async/await` end-to-end** on every I/O path — no `.Result`, no `.Wait()`, no `async void` outside event handlers (explicit bonus criterion: TPL usage).
- **Custom exceptions with clear messages**: `ConfigurationKeyNotFoundException`, `ConfigurationTypeMismatchException`.
- **Comments explain WHY**, and flag .NET idioms that differ from Node.js expectations (the maintainer's background is NestJS/TypeScript) — e.g. `IHostedService` vs. NestJS lifecycle hooks, `Interlocked.Exchange` vs. single-threaded event loop assumptions.
- The case's public surface is frozen: ctor `ConfigurationReader(applicationName, connectionString, refreshTimerIntervalInMs)` and exactly one public method `T GetValue<T>(string key)`.

## 5. Working Rules

- Architecture questions **before** implementing; ask "which module / new or existing / how does this integrate" when structure is ambiguous.
- Show code structure first, then implementation. Production-ready code only.
- **End of every phase** (use the `phase-docs` skill): write `docs/phases/phase-N.md`, update the phase table above, write/update ADRs for any architectural decision made during the phase.
- **Before every phase commit**: run the `case-compliance` skill (hard-requirement scan) and the `dotnet-reviewer` skill.
- Conventional commits, one per phase: `feat(phase-1): storage layer with mongo provider`.
- Commits carry the user's identity only — no AI co-author trailers.
- TDD for the core library: tests first, mocked `IConfigurationStorageProvider`, no MongoDB dependency in unit tests.

## 6. Recommended Reading Order

1. **CLAUDE.md** (this file) — constitution + phase table
2. **[docs/architecture.md](docs/architecture.md)** — end-to-end picture
3. **[ADR 0002](docs/adr/0002-atomic-snapshot-swap.md) + [ADR 0003](docs/adr/0003-polling-plus-broker-hybrid.md)** — the two core architectural decisions
4. **docs/phases/phase-3.md** — the most critical phase story (refresh & resilience), once it exists
5. **tests/DynamicConfig.Library.Tests** — behavior specification in executable form

## 7. Local Environment Notes

- .NET SDK 8.0.422 installed at `C:\Program Files\dotnet` but **not on PATH in fresh shells** — prefix commands with `$env:PATH = "C:\Program Files\dotnet;$env:PATH"`.
- NuGet had zero configured sources on this machine; `nuget.org` was added manually (`dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`).
- Case source PDF (Turkish): `C:\Users\Nurie\Downloads\Backend Developer Code Case.pdf`.
