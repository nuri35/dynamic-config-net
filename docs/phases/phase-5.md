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

## What was built

*(to be filled as implementation lands)*

## Key decisions

*(beyond the ratified design above — to be filled during implementation)*

## Test coverage

*(to be filled — expected: publisher failure-isolation tests, consumer match/drop/degradation tests against a faked broker seam, idempotency proof)*

## Deviations from plan

*(to be filled)*
