# Audit Block 3 — Broker (Phases 5.1–5.3) · 2026-07-04

Same protocol as [Block 1](block-1.md)/[Block 2](block-2.md); this block is mostly LIVE verification reusing the 5.3 drill tooling (DemoService `GET /` probe + management API). Preflight: native Mongo `Disabled`; native RabbitMQ service found **running again** (StartType Automatic survived a reboot — the CLAUDE.md "stopped for now" note materialized as a hazard) → stopped before the drill; docker (`com.docker.backend`) confirmed the only listener on 5672/15672/27017.

Fleet: WebUI `:5199` (publisher), DemoService ×3 — SERVICE-A `:5301` + `:5302`, SERVICE-B `:5303` — all consumers with `DYNAMIC_CONFIG_RABBITMQ_URI` set and a **60 000 ms** poll floor, so every sub-second observation is provably broker-carried. Latencies below are measured from just-after-the-PUT-returned by tight-polling `GET /` (~5–20 ms probe resolution included).

## PART A — Drift check (yesterday's claims, post Block 1–2 commits)

| # | Item | Evidence | Verdict |
|---|---|---|---|
| 1 | Blocks 1–2 touched zero broker files | `git diff --name-only f0c260c..HEAD`: 11 files — audit docs, README, WebUI contract doc-comment, exception XML docs, tests. Nothing under `Messaging/`, no publisher/consumer/DemoService file | ✓ (A2–A4 ran as confirmations) |
| 2 | Steady-state broker latency | One UI edit (`PUT` MaxItemCount) → served by consumers in **69–144 ms** vs the 60 000 ms poll floor — same order of magnitude as 5.3's 6–125 ms | ✓ |
| 3 | Ghost-fix permanence | Full outage/recovery cycle (`docker stop`/`start`) + post-heal publish → management API shows exactly **3** connections: 2 consumers + **exactly 1 publisher**. No ghost | ✓ |
| 4 | Poison message | 1 garbage payload (`this is not json {{{`) published onto the fanout, `routed:true` to all 3 queues → all 3 consumers stayed attached; next real event served in **69 ms** | ✓ |

## PART B — Fanout's core claim, first live proof (multi-instance)

| # | Scenario | Evidence | Verdict |
|---|---|---|---|
| 5 | TWO SERVICE-A instances, one edit → BOTH refresh | Baseline: 3 server-named exclusive `amq.gen-*` queues, 1 consumer each. One `PUT` → **both** instances served the new value **144 ms** after the PUT returned; both console logs show the flip in the same second (`[05:28:12] … MaxItemCount=77` in a1.log AND a2.log). A shared-queue misconfiguration would have delivered to exactly one | ✓ |
| 6 | Mixed apps: A edit beside a B consumer | SERVICE-B instance's values byte-identical after the A edit (silent drop — the foreign-drop trace goes to the `Trace` channel, no console listener attached by design). Zero-Mongo-reads is pinned at the seam by `ForeignApplicationEvent_TriggersNoRefresh` (unit); isolation additionally visible in the probe set (B sees only `IsBasketEnabled`) | ✓ |
| 7 | Instance death | Killed one SERVICE-A process → queue count **3 → 2** (exclusive auto-delete did its job, management API); survivor served the next edit in **74 ms** | ✓ |
| 8 | Queue-count sanity | Whole drill accumulated zero orphans: after stopping every consumer → **0 queues, 0 connections** | ✓ |

## PART C — Boundary corners

| # | Corner | Evidence | Verdict |
|---|---|---|---|
| 9 | Write DURING broker restart | Write with broker stopped: **200 in 4.08 s** (one AMQP connect-timeout paid, warning logged, write landed). Six writes fired across the container-starting window (port open, AMQP not yet ready): **all 200, 3–51 ms each** — the rebuild attempt fails fast and the warning path absorbs it; 4 `publish failed … polling will deliver` warnings total in the WebUI log, zero hangs, zero 500s. After healthy: next edit served in **75 ms** (self-heal, no recycle) | ✓ |
| 10 | Garbage `DYNAMIC_CONFIG_RABBITMQ_URI` | **REAL BUG — fixed TDD-style.** `"not-a-uri"` threw `UriFormatException` out of the frozen public ctor (`new Uri` inside `CreateChangeSourceFromEnvironment`): the decision-8 asymmetry held for *absent* config but not *invalid*. Failing test first (`MalformedBrokerUri_ReaderBootsPollingOnlyInsteadOfThrowing`, 2 cases) — red run also took down 4 unrelated tests whose readers were constructed while the env var was polluted, demonstrating the blast radius live. Fix: `Uri.TryCreate` guard → warning + polling-only; raw value deliberately not echoed (broker URIs carry credentials). `"amqp://nonexistent-host:5672"` was already safe (Uri parses; connect failure lands in `StartConsumerSafelyAsync`'s catch — covered by the existing broker-down pin). ADR 0005 + CLAUDE.md decision 8 updated in the same commit | FIXED |

## Closure

- **One real library defect found and fixed** (C10): malformed broker URI failed the boot; now warn + polling-only, pinned by a 2-case theory.
- Scenarios B5–B7 added to phase-5.md as the **Phase 6 rerun additions** (M1–M4).
- State restored: `MaxItemCount` back to `50`, all drill processes stopped, 0 queues/connections left on the broker, native RabbitMQ service stopped (still `Automatic` start — flagged below).
- 213/213 tests green; frozen surface character-identical; compliance PASS.

**Operational flag for the user:** the native Windows RabbitMQ service is `StartType: Automatic` — it came back after a reboot and will again. Yesterday's "stopped for now" doesn't survive restarts; consider `Set-Service RabbitMQ -StartupType Disabled` (as was done for MongoDB) before the Phase 6 compose work.
