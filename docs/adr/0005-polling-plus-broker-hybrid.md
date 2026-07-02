# ADR 0005: Polling + Message Broker Hybrid Refresh

- **Status:** Proposed — EXTRA feature, ratified when Phase 5 starts (the polling baseline itself is a mandatory requirement and ships in Phase 3)
- **Date:** 2026-07-02 (drafted in Phase 0 planning; broker implementation deferred to Phase 5)

## Context

The case **requires** periodic polling: "the system must check for new records and record changes at the parametrically given interval" (`refreshTimerIntervalInMs`). Separately, "using a message broker" is an extra-point item. Pure polling means worst-case staleness of one full interval; pure eventing means a lost message or a dead broker silently serves stale config forever.

## Decision

Both, layered — **polling is the guaranteed baseline, the broker is a latency optimization**:

- The library polls storage every `refreshTimerIntervalInMs` (required behavior, always on).
- The WebUI publishes a `config-changed` event to a RabbitMQ **fanout exchange** on every create/update; each library instance consumes it and triggers an immediate refresh.
- The event carries **no configuration data** — it is a signal, not state. The refresh it triggers runs the same storage-query code path as the poller. MongoDB stays the single source of truth.

## Consequences

**Positive**
- Change propagation is milliseconds when the broker is healthy, and never worse than one poll interval when it isn't.
- Lost, duplicated, or reordered messages are harmless: refresh is idempotent, and polling backstops any loss. No message-ordering or exactly-once machinery needed.
- Fanout exchange means zero routing configuration; every subscribed reader instance refreshes, and each instance still applies its own `ApplicationName` filter at the query (isolation is unaffected by broadcasting).
- Broker connection failure degrades gracefully to the required polling behavior — the bonus feature can never make the required feature worse.

**Negative**
- One more infrastructure piece (RabbitMQ) in docker-compose — acceptable, it is an explicit bonus target.
- Fanout wakes readers whose application's records didn't change; they run one cheap indexed query and swap an equivalent snapshot. Fine at this scale; a topic exchange keyed by `ApplicationName` is the documented upgrade path.

## Alternatives Considered

- **Polling only** — meets the letter of the case but leaves user-visible staleness up to a full interval; forfeits the broker bonus.
- **Broker only** — violates the case's explicit polling requirement and turns broker downtime into indefinite staleness.
- **Fat events (config payload in message)** — creates a second source of truth, ordering hazards, and stale-overwrite races. Signals-only avoids the entire class.
