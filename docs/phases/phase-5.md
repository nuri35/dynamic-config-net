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

# Phase 5.2 — Broker Consumer (library side) *(pending)*

Gate: resolve the two reserved questions with the user first — (1) the broker-address channel vs the case-frozen 3-param ctor (options in ADR 0005), (2) exchange-constant placement (library shared-kernel vs duplicate-and-verify). Then: consumer binding an exclusive auto-delete queue, silent drop on foreign application names, match → existing `RefreshSnapshotAsync`, graceful polling-only degradation without the broker.
