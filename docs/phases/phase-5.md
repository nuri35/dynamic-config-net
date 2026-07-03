# Phase 5 — RabbitMQ Instant Refresh (EXTRA)

**Status:** in progress · **Kickoff (docs-only):** 2026-07-03 · **Commit:** `docs(phase-5): ratify broker architecture (ADR 0005) — no code`

## Goal

Add the speed layer on top of the mandatory polling: the WebUI publishes a `config-changed` signal after every successful write; each reader instance early-triggers its existing refresh on matching events. Propagation shrinks from ≤1 poll interval to sub-second — while polling remains the guaranteed-convergence base layer, so the broker can never make the required behavior worse.

## Ratified design (docs-first — [ADR 0005](../adr/0005-polling-plus-broker-hybrid.md), accepted 2026-07-03)

- **Hybrid model:** polling = correctness/reliability base (guaranteed ≤1 interval, parametric per the case); broker = accelerator, **never a dependency**. Publish failure never fails a write (log-and-continue); a consumer without broker access degrades to polling-only.
- **Topology:** one **fanout** exchange; each reader instance binds an **exclusive auto-delete** queue (dies with the instance, no orphans). Fanout over direct/topic because config changes are rare and consumer-side filtering is free; topic-with-routing-key=`applicationName` is the recorded at-scale alternative.
- **Thin event ("notify, don't transfer"):** payload is `{ applicationName, occurredAtUtc }` — no values in flight. Mongo stays the single source of truth; redelivery is harmless (idempotent refresh); each event costs one cheap Mongo read, accepted.
- **Consumer behavior:** foreign `applicationName` → drop silently (no refresh, no read). Match → trigger the **existing** `RefreshSnapshotAsync` (Phase 3) — same query, same atomic swap, same last-good fallback. No new data path.

## Open decision (PENDING — resolve with the user at implementation start)

The case-frozen 3-param ctor has no broker-address slot. Candidates (listed, not chosen): optional config/environment mechanism outside the ctor; deriving from a combined connection string; a library-level opt-in API leaving the frozen surface untouched. **Implementation does not start until this is decided.**

---

# Phase 5.1 — Broker Publisher (WebUI side)

**Status:** done · **Completed:** 2026-07-03 · **Commit:** `feat(phase-5.1): rabbitmq config-changed publisher with fire-and-forget discipline`

## What was built

- **`IConfigurationChangePublisher`** (`Messaging/`) — the publish seam (one method: `PublishChangedAsync(applicationName, ct)`), same seam discipline as ADR 0003: the service depends on the interface, tests mock it, transport is swappable.
- **`RabbitMqConfigurationChangePublisher`** — RabbitMQ.Client v7 (async-native API, no sync-over-async anywhere): lazy connection (constructor never touches the network — the WebUI boots and serves fully with the broker down), idempotent fanout exchange declare on first use, automatic-recovery enabled plus rebuild-on-next-publish for a dead channel, `IAsyncDisposable`. Wire contract isolated in `BuildEventBody`: exactly `{ applicationName, occurredAtUtc }`, camelCase JSON.
- **`RabbitMqBrokerDefaults`** — the exchange name constant (`dynamicconfig.config-changed`), WebUI-local **by 5.1's own rule** (no library changes); see the flagged question below.
- **Service integration** — `CreateAsync`/`UpdateAsync` call `PublishChangeSafelyAsync` strictly AFTER the successful Mongo write; the helper catches every publish exception, logs it with application-name context, and continues. No event is ever published for rejected writes (validation/not-found throw before the publish line).
- **Infrastructure** — `rabbitmq:3-management` service in docker-compose with a `rabbitmq-diagnostics ping` healthcheck; broker address from `ConnectionStrings:RabbitMq` (appsettings default `amqp://guest:guest@localhost:5672`, env-overridable via `ConnectionStrings__RabbitMq`).

## Key decisions

- **Fire-and-forget over outbox.** A publish failure is logged and swallowed — the HTTP response reflects the Mongo outcome only. The transactional-outbox pattern (table + relay guaranteeing every write eventually emits its event) is the known heavyweight alternative, rejected for scope: polling already IS the guaranteed carrier, so a lost signal costs at most one poll interval — an outbox would add machinery to prevent a harm that does not exist here (also recorded in ADR 0005's alternatives).
- **Exchange-constant placement — FLAGGED, not decided.** The 5.2 consumer (library side) must reference the same exchange name. Moving the constant into the library (the `MongoConfigurationStorageDefaults` shared-kernel pattern) is a public-surface decision reserved for the user at 5.2 kickoff; 5.1 keeps it WebUI-local because 5.1 changes no library code.
- **Lazy, self-healing connection.** No connection at boot (broker-down boot must not degrade the CORE admin surface); a closed channel/connection is disposed and rebuilt on the next publish, so a broker restart heals without recycling the WebUI — proven live in the smoke.
- **The caller owns the failure policy.** The publisher throws honestly; the SERVICE decides that a failed signal never fails a write. Keeps the seam reusable (a future caller may want the exception) and the business rule where business rules live.

## Test coverage

`dotnet test` → 172/172 green (96 library + 76 WebUI). New behavior proven with the mocked publisher:

- Create success → published exactly once with the right `applicationName`; update success → same.
- Validation failure and not-found → publisher **never** called (no events for rejected writes).
- Publisher throws → create and update still succeed and persist (fire-and-forget pinned).
- Wire contract: `BuildEventBody` emits exactly `applicationName` + `occurredAtUtc` and nothing else (thin-event pin — any third field is a test failure).

Live smoke against the container: observer queue bound to the fanout exchange received exactly one message with the exact thin payload after a UI-path create; broker stopped → create still 201 with the contextual warning logged (`BrokerUnreachableException`); broker restarted → next publish healed and landed without recycling the WebUI. Environment note: a **native Windows RabbitMQ service was found running and stopped** before the smoke — the same port-shadowing hazard as the Mongo finding (Phase 4.3); only the compose broker may listen on 5672.

## Deviations from plan

None — no consumer, no library changes, no full-compose orchestration beyond the rabbitmq service. The reader remains byte-identical to CORE.

---

# Phase 5.2 — Broker Consumer (library side)

**Status:** done · **Completed:** 2026-07-03 · **Commit:** `feat(phase-5.2): env-var opt-in broker consumer with polling-only degradation`

Both reserved questions were resolved by the user at kickoff: (1) broker address via the `DYNAMIC_CONFIG_RABBITMQ_URI` environment variable; (2) exchange constant moves into the library as shared kernel.

## The E decision — `DYNAMIC_CONFIG_RABBITMQ_URI`

**Semantics:** present and non-blank → the reader starts the instant-refresh consumer next to polling (hybrid mode); absent or blank → no consumer object is ever constructed — pure polling, exactly Phase 4 behavior, zero broker code on any path. Absence is a *mode*, not an error.

**Rationale:** the case-frozen 3-param constructor stays **character-identical** (verified by compliance scan); opt-in costs consumers nothing (no signature change, no recompile of existing callers); environment-variable configuration is the 12-factor idiom and composes naturally with docker-compose (Phase 6); graceful-by-absence means the bonus feature literally cannot regress the required one.

**Known weakness + antidotes:** an environment variable is an *invisible contract* — nothing in the API surface hints it exists. Antidotes shipped with it: (a) a dedicated README section under Usage (name, format, example, with/without behavior), (b) the constant `RabbitMqBrokerDefaults.BrokerUriEnvironmentVariableName` is public with full XML docs, (c) one startup `Trace` line always states the mode ("broker URI found — instant-refresh consumer started" / "no broker URI — polling-only mode"), so the reader's mode is observable by any host that attaches a trace listener.

## What was built

- **Shared kernel moved into the library** (`Messaging/`): `RabbitMqBrokerDefaults` (exchange name + env-var name) and `ConfigurationChangedEvent` (the record IS the wire contract: `ToUtf8Json()` for the publisher, `TryParse()` for the consumer — the WebUI's local copies were deleted and its publisher now serializes the library type, so publisher/consumer drift is impossible at compile time). Same public-surface trade-off as 4.1's constants move: wider library surface bought for cross-context consistency — known, accepted, flagged here.
- **`IConfigurationChangeSource`** — the internal consumer seam (mirror of ADR 0003): `StartAsync(callback, ct)`, mockable, `IAsyncDisposable`. The broker integration stays an implementation detail behind the frozen surface.
- **`RabbitMqConfigurationChangeSource`** — server-named **exclusive auto-delete** queue bound to the shared fanout exchange (dies with the instance, no orphans); `AutomaticRecoveryEnabled` for mid-life drops (while down, polling carries — no custom retry machinery, consistent with 5.1's no-outbox); **autoAck with no nack/requeue choreography** — a failed refresh after a message is not broker-retried, the next poll self-heals; malformed bodies are parse-or-drop (`TryParse` + trace warning) so one bad message never kills the consumer.
- **`ConfigurationReader` integration** — internal ctor gains an optional `IConfigurationChangeSource?` (test seam; public ctor untouched). Consumer start is backgrounded (`_consumerStart`) and failure-swallowed; on message: foreign name → silent drop with trace, own name → the **existing** `RefreshSnapshotAsync`. `Dispose`/`DisposeAsync` extend to the consumer (await start, dispose source, double-dispose safe). Internal observability for tests: `IsInstantRefreshConfigured`, `WaitForConsumerStartAsync()`.
- **DemoService** — `DYNAMIC_CONFIG_RABBITMQ_URI` added to `launchSettings.json` (http profile) for the 5.3 demo; no other changes.

## Key decisions

- **The fail-fast asymmetry (ADR 0004 vs the broker), stated for the record:** Mongo unreachable at construction → **throw** — Mongo is the *data source*; without one successful load the reader has nothing to serve and every `GetValue` would lie. Broker unreachable at construction → **log and continue polling-only** — the broker is the *accelerator*; its absence costs latency (≤1 poll interval), never correctness. Same event, opposite policy, because the two dependencies hold opposite roles. Pinned by test (`BrokerUnreachableAtStart_ReaderBootsPollingOnly`).
- **No refresh serialization (concurrency guard verified, not assumed):** a broker-triggered and a timer-triggered refresh may run concurrently. Analysis: each `RefreshSnapshotAsync` builds a *complete* snapshot and publishes via `Volatile.Write` — last-write-wins on the reference, torn state impossible; the only theoretical edge is a slower-but-staler fetch landing after a faster-fresher one, a sub-interval regression the next poll self-heals. A refresh gate would add contention for no practical gain → none added; pinned by the 20-generation concurrent-refresh consistency test.
- **No nack/requeue:** autoAck by design. Broker redelivery machinery would duplicate what polling already guarantees, and a poison message that repeatedly failed refresh would loop forever; drop-and-let-polling-carry is strictly simpler with identical convergence.
- **Consumer start backgrounded via `Task.Yield`:** the ctor returns immediately (no broker I/O on the construction path, no new sync bridges); the start task never faults, and `DisposeAsync` awaits it before disposing the source — no orphaned starts.
- **Consumer placement — inside the library, next to the snapshot.** The alternative (a host-side/sidecar listener) was not viable: the thing a signal refreshes is the reader's private in-memory snapshot, and only the reader can swap it. The listener therefore lives where the state lives — `ConfigurationReader` owns both refresh triggers (timer and broker) exactly as it owns the snapshot they feed.

## Test coverage

`dotnet test` → 187/187 green (112 library + 75 WebUI). New behavior proven against the mocked seam (no live RabbitMQ in unit tests):

- Matching event → exactly one storage fetch, fresh value served; foreign event → zero fetches (silent drop).
- Broker down at start (fake throws) → ctor survives, CORE serves; env var set to a dead port with the **real** RabbitMQ source → ctor does not throw, `GetValue` works (polling-only).
- Env var absent → `IsInstantRefreshConfigured == false`, no consumer exists (pins Phase 4 behavior).
- `DisposeAsync` disposes the change source; double-dispose safe.
- Concurrency: 20 generations of simultaneous broker + manual refreshes — both keys always from one generation.
- Wire contract (shared type): serialization emits exactly two camelCase fields; `TryParse` round-trips, rejects malformed JSON / missing / blank names without throwing, tolerates extra fields (forward compatibility).

**Live smoke (timing evidence for 5.3):** DemoService with `DYNAMIC_CONFIG_RABBITMQ_URI` set and the poll interval deliberately stretched to **60 000 ms**; a WebUI edit of `MaxItemCount` was served by the reader in **1 037 ms** end-to-end (HTTP write + Mongo + publish + fanout + refresh + observer polling overhead) — 58× faster than the poll floor, provably broker-triggered.

## Deviations from plan

None beyond the pre-flagged shared-kernel move (exchange constant + wire contract into the library, per the user's instruction). The frozen surface is character-identical; compose untouched beyond 5.1's rabbitmq service; no e2e demo pass (that's 5.3).
