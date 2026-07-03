# ADR 0005: Polling + Message Broker Hybrid Refresh

- **Status:** Accepted — ratified 2026-07-03 at the Phase 5 kickoff (docs-first; implementation follows this document)
- **Date:** 2026-07-02 (drafted in Phase 0 planning) · ratified 2026-07-03

## Context

The case **requires** periodic polling: "the system must check for new records and record changes at the parametrically given interval" (`refreshTimerIntervalInMs`). Separately, "using a message broker" is an extra-point item. Pure polling means worst-case staleness of one full interval; pure eventing means a lost message or a dead broker silently serves stale config forever.

## Decision

Both, layered — **polling is the correctness/reliability base layer, the broker is the speed layer**:

### Hybrid model — the broker is an accelerator, never a dependency

- The library polls storage every `refreshTimerIntervalInMs` (required behavior, always on). Polling guarantees convergence within ≤1 interval no matter what the broker does.
- The WebUI publishes a `config-changed` event to RabbitMQ on every successful create/update; each library instance consumes it and triggers an immediate refresh, shrinking the propagation window from "up to one interval" to sub-second.
- **Failure policy, write side:** a publish failure never fails the write — log and continue; polling will carry the change. The HTTP response reflects the Mongo outcome only.
- **Failure policy, read side:** a consumer that cannot reach the broker (at startup or mid-run) degrades gracefully to polling-only behavior — the CORE contract, unchanged. The bonus feature can never make the required feature worse.

### Topology — one fanout exchange, per-instance exclusive queues

- A single **fanout** exchange; every reader instance binds its own **exclusive auto-delete** queue, so a queue dies with its instance and no orphan queues accumulate.
- Fanout deliberately over direct/topic: config changes are rare (a few events per day), so consumer-side filtering costs approximately nothing, and fanout keeps the topology single-pattern and bind-and-forget — no routing keys to configure, migrate, or get wrong.

### Thin event — "notify, don't transfer"

Payload is `{ applicationName, occurredAtUtc }` and **nothing else**. No configuration values ever transit the queue.

- **Single source of truth stays Mongo:** a redelivered or stale event cannot overwrite fresh data — it only says "go look", and every look fetches the latest state.
- **Idempotency comes free** under at-least-once delivery: a double refresh fetches the same fresh data twice; zero harm. No dedup, ordering, or exactly-once machinery needed.
- **No sensitive values in flight** through the broker.
- Trade-off, accepted: each event costs one Mongo read (the refresh). Negligible at config-change frequency.

### Consumer behavior — early-trigger the existing path, add no new data path

On message: if `event.applicationName` ≠ own application name → **drop silently** (no refresh, no Mongo read). If it matches → trigger the **existing** `RefreshSnapshotAsync` path from Phase 3 — the same storage query, the same atomic snapshot swap, the same last-good fallback. The broker adds no second way for data to enter the reader.

Isolation note: a foreign event carries only an application name, never values, so observing and dropping it violates nothing — and the refresh it would have triggered still runs the `(ApplicationName, IsActive)`-filtered query anyway.

## Pending detail — resolved at implementation start, with the user

The case-frozen 3-parameter constructor (`applicationName, connectionString, refreshTimerIntervalInMs`) has **no broker-address parameter**, so where the consumer's RabbitMQ address comes from is deliberately **unresolved** in this ADR. Candidate options (listed, not decided):

1. An optional configuration/environment mechanism outside the constructor (e.g. an environment variable the library reads if present).
2. Deriving the broker address from a combined/extended connection string.
3. A library-level opt-in API (e.g. an optional method or factory) that leaves the frozen 3-param surface untouched.

**Do not implement any of these before the decision is made with the user.**

## Consequences

**Positive**
- Change propagation is milliseconds when the broker is healthy, and never worse than one poll interval when it isn't.
- Lost, duplicated, or reordered messages are harmless: the event is a signal, refresh is idempotent, and polling backstops any loss.
- Zero routing configuration; every subscribed instance refreshes on its own application's events and cheaply drops the rest; isolation is unaffected by broadcasting.
- Broker connection failure degrades to exactly the required CORE behavior on both the write and read sides.

**Negative**
- One more infrastructure piece (RabbitMQ) in docker-compose — acceptable, it is an explicit bonus target.
- Fanout wakes readers whose application's records didn't change; they drop the event after a string comparison. Fine at this scale.

## Alternatives Considered

- **Polling only** — meets the letter of the case but leaves user-visible staleness up to a full interval; forfeits the broker bonus.
- **Broker only** — violates the case's explicit polling requirement and turns broker downtime into indefinite staleness.
- **Topic exchange with routing key = applicationName** — the right switch at scale (hundreds of applications, events per second: broker-side filtering saves consumer wakeups and network fan-out). At this project's event rate it buys nothing and adds routing configuration to provision and keep in sync; recorded as the documented upgrade path, not chosen.
- **Fat events (config payload in message)** — creates a second source of truth, ordering hazards, and stale-overwrite races; would also put config values on the wire. "Notify, don't transfer" avoids the entire class.
- **Transactional outbox / retry queue for the publisher** — the heavyweight guarantee that every write eventually produces its event (outbox table + relay). Rejected for scope: polling already IS the guaranteed carrier, so a lost signal costs at most one poll interval of latency — the outbox would add a table, a relay loop, and failure modes of its own to protect against a harm that does not exist here. Publish failures are log-and-continue (5.1).
