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
| 4 | Fail-fast on initial load | "Last successful records" presupposes one success; config-less service misbehaves anyway; boot-throw composes with orchestrator restarts | [ADR 0004](docs/adr/0004-fail-fast-initial-load.md) |
| 5 | Polling + RabbitMQ hybrid refresh — broker is an ACCELERATOR, never a dependency | Broker = sub-second latency; polling = guaranteed convergence ≤1 interval; publish failure never fails a write; consumer without broker degrades to polling-only | [ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md) (accepted) |
| 6 | Broker topology: one fanout exchange + per-instance exclusive auto-delete queues | Config changes are rare — consumer-side filtering costs nothing; fanout is single-pattern, bind-and-forget; topic-with-routing-key is the documented at-scale alternative | [ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md) |
| 7 | Thin event — `{ applicationName, occurredAtUtc }`, never values ("notify, don't transfer") | Mongo stays single source of truth; idempotent under at-least-once redelivery; no sensitive values in flight; consumer match → existing `RefreshSnapshotAsync`, mismatch → silent drop | [ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md) |
| 8 | Consumer opt-in via `DYNAMIC_CONFIG_RABBITMQ_URI` env var — absence is a mode, not an error | Case-frozen ctor stays character-identical; set → hybrid, absent/blank → polling-only (Phase 4 byte-identical); broker down at boot **or malformed URI** → log + polling-only (fail-fast is Mongo-only, ADR 0004 asymmetry; malformed-URI clause added by Block 3 audit); startup trace line states the mode | [ADR 0005](docs/adr/0005-polling-plus-broker-hybrid.md) |

Rules: changing a locked decision requires the user's explicit approval. Any decision made or revised mid-phase updates this table **and** its ADR in the same commit.

## 3. Phase Table

### CORE — mandatory requirements (submittable after Phase 4)

| Phase | Scope | Status | Completed | Outcome | Doc |
|---|---|---|---|---|---|
| 0 | Scaffold, docs workflow, README v1 | done | 2026-07-02 | Solution + 4 projects build clean; mongo-only compose; constitution, ADRs, skills/hook | [phase-0](docs/phases/phase-0.md) |
| 1 | Domain & storage (Mongo provider, compound index, query-level filtering) | done | 2026-07-02 | Record model + provider seam + Mongo provider; isolation proven at query level by rendered-filter test; 30/30 unit tests, no DB dependency | [phase-1](docs/phases/phase-1.md) |
| 2 | Core reader (`GetValue<T>`, conversion engine, TDD, mocked provider) | done | 2026-07-02 | Case-exact public surface + internal DI ctor; FrozenDictionary snapshot; strict conversion engine (invariant culture, bool 1/0, 3 custom exceptions); 59/59 tests, zero DB | [phase-2](docs/phases/phase-2.md) |
| 3 | Refresh & resilience (polling, snapshot swap, fallback, ADR 0004) | done | 2026-07-02 | PeriodicTimer loop + Volatile.Read/Write swap + last-good fallback + IDisposable/IAsyncDisposable; ADR 0004 accepted (fail-fast); 69/69 tests | [phase-3](docs/phases/phase-3.md) |
| 4.1 | Web UI backend (admin repository & service layer, write-side validation) | done | 2026-07-03 | Admin repo/service as own bounded context (all apps, inactive incl., read-write); validation reuses library parsing via extracted public `ConfigurationValueParser`; UTC `LastModifiedDate` stamping pinned by test; IsActive defaults true on create; early id-format validation; 138/138 tests | [phase-4](docs/phases/phase-4.md) |
| 4.2 | Web UI REST API (controllers, DTOs, HTTP error mapping) | done | 2026-07-03 | Thin `ConfigurationsController` (list/get/create 201/strict update); DTOs keep entity off the wire, server-owned fields absent by type; `IExceptionHandler` → RFC 7807 ProblemDetails (400+fieldName, 404+recordId, 500 leaks nothing); Swagger at `/swagger` with XML summaries; 161/161 tests + HTTP smoke test | [phase-4](docs/phases/phase-4.md) |
| 4.3 | Web UI frontend (list/add/update, client-side name filter) | done | 2026-07-03 | Vanilla JS SPA from wwwroot (no framework/build, rationale documented); in-memory name filter with zero fetches on the filter path; type dropdown; field-level 400 errors via `fieldName`; write-side concurrency model documented; browser smoke pass caught and fixed the missing update-path IsActive channel (now tri-state on both write verbs); 165/165 tests. **CORE COMPLETE — ✅ CHECKPOINT reached** | [phase-4](docs/phases/phase-4.md) |

**✅ CHECKPOINT after Phase 4: every mandatory case requirement is met — project is submittable. EXTRA work needs the user's explicit go-ahead.**

### EXTRA — bonus features (only if time allows, in this order)

| Phase | Scope | Status | Completed | Outcome | Doc |
|---|---|---|---|---|---|
| 5.1 | RabbitMQ publisher (WebUI side: seam + fanout publish, fire-and-forget) | done | 2026-07-03 | `IConfigurationChangePublisher` seam + lazy self-healing RabbitMQ impl; publish AFTER Mongo success, failure = log-and-continue (201 stays 201, proven live with broker stopped); thin event pinned by test; rabbitmq:3-management in compose; 172/172 tests. Exchange-constant placement flagged for 5.2 | [phase-5](docs/phases/phase-5.md) |
| 5.2 | RabbitMQ consumer (library side: exclusive queue, match → refresh, polling-only degradation) | done | 2026-07-03 | Env-var opt-in (`DYNAMIC_CONFIG_RABBITMQ_URI`, decision 8); shared kernel moved to library (`RabbitMqBrokerDefaults` + `ConfigurationChangedEvent` = the wire contract, WebUI publisher now serializes the same type); internal `IConfigurationChangeSource` seam + RabbitMQ impl (exclusive auto-delete queue, autoAck/no-nack, parse-or-drop); frozen ctor character-identical; no refresh gate (verified + pinned); 187/187 tests; live smoke 1 037 ms vs 60 000 ms poll floor | [phase-5](docs/phases/phase-5.md) |
| 5.3 | Broker e2e proof + Phase 5 closure | done | 2026-07-03 | 11-scenario live drill all PASS (steady-state broker path 6–125 ms vs 30 s floor; outage → polling carried in 15.2 s; recovery self-healed both sides; poison messages dropped; env-var-absent = zero AMQP sockets). Smoke-caught fix: publisher ghost connection per outage cycle (`AutomaticRecoveryEnabled` off — manual rebuild is the single mechanism; consumer keeps recovery). Frozen surface re-confirmed. **PHASE 5 COMPLETE** | [phase-5](docs/phases/phase-5.md) |
| 6 | Full docker-compose ecosystem (mongo + rabbitmq + webui + demoservice) | done | 2026-07-04 | Two multi-stage Dockerfiles (repo-root context, cached csproj restore) + `.dockerignore`; compose gains webui/demoservice with health-gated `depends_on`, `__`-nested env wiring, `restart: unless-stopped`; from-scratch drill (down -v + rmi + literal `docker compose up -d --build`) → 4 healthy, empty DB → seed via UI → demo pickup, edit **75 ms** via broker. Block-4 bug caught+fixed: rabbitmq healthcheck `ping`→`check_port_connectivity` (readiness race left the demo silently polling-only on first boot); EDB Apache 8080 host-shadow flagged. No app code touched; 213/213 | [phase-6](docs/phases/phase-6.md) |
| 7 | Documentation polish (README final pass, diagrams, coverage checklist) | done | 2026-07-04 | Stale "planned" language swept (architecture.md EXTRA section, ADR 0002); architecture.md broker-address "pending detail" resolved to decision 8 wording incl. malformed-URI clause, README env-var table matched; coverage tables gained evidence pointers (213 tests, evaluator sim 7/7). **PROJECT COMPLETE** | [phase-7](docs/phases/phase-7.md) |

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
4. **[docs/phases/phase-3.md](docs/phases/phase-3.md)** — the most critical phase story (refresh & resilience)
5. **tests/DynamicConfig.Library.Tests** — behavior specification in executable form

## 7. Local Environment Notes

- .NET SDK 8.0.422 installed at `C:\Program Files\dotnet` but **not on PATH in fresh shells** — prefix commands with `$env:PATH = "C:\Program Files\dotnet;$env:PATH"`.
- NuGet had zero configured sources on this machine; `nuget.org` was added manually (`dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`).
- Case source PDF (Turkish): `C:\Users\Nurie\Downloads\Backend Developer Code Case.pdf`.
- **Mongo: docker-only on this machine (2026-07-03).** The native Windows `MongoDB` service is stopped and set to `Disabled` after the e2e shadow-Mongo finding (two listeners on 27017 fail over silently). Use `docker compose up -d`; the container DB carries the seeded demo dataset (SERVICE-A/B records).
- **RabbitMQ: same shadow hazard — now Stopped + Disabled (2026-07-04).** The native Windows `RabbitMQ` service's `Automatic` start survived a reboot and re-shadowed 5672 (found live in the Block 3 audit); it is now `StartupType Disabled`, same treatment as MongoDB. Only the compose `dynamicconfig-rabbitmq` may listen on 5672.
- **Port 8080 shadow — EDB Postgres Enterprise Manager Apache `httpd` (2026-07-04).** A third-party `edb\pem\httpd\apache\bin\httpd.exe` binds host `8080` and shadows the WebUI container's port map — `http://localhost:8080` reaches Apache, not the WebUI. Left running (unrelated software, operator's call); verify the WebUI over the compose network or free 8080 first. Demo on 8081 is unaffected.
