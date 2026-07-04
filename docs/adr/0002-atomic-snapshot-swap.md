# ADR 0002: Atomic Immutable-Snapshot Swap for Concurrency

- **Status:** Accepted
- **Date:** 2026-07-02 (Phase 0; implemented in Phases 2–3)

## Context

`GetValue<T>` is called from arbitrary application threads on the hot path, while a background loop periodically replaces the entire config set. "Preventing possible concurrency problems" is an explicit extra-point criterion. In multi-threaded .NET — unlike Node.js's single-threaded event loop, where this safety comes for free — concurrent read/write over shared mutable state must be engineered deliberately.

## Decision

The current configuration lives in an **immutable dictionary snapshot** referenced by a single field.

- **Read path:** `GetValue<T>` copies the snapshot reference to a local with `Volatile.Read` **once at entry** and works only with that local — the field is never re-read mid-operation, so one call always sees one generation. No locks, no I/O, no awaiting.
- **Write path:** the refresh loop builds a *complete new* immutable snapshot (a `FrozenDictionary`, chosen in Phase 2) off to the side, then publishes it with `Volatile.Write` — the read/write pair forms the memory barrier that guarantees construction-happens-before-publication visibility across cores. There is exactly one writer (the refresh loop; `PeriodicTimer` ticks cannot overlap), many readers.

## Consequences

**Positive**
- Readers see either the entire old snapshot or the entire new one — a torn, half-updated view is structurally impossible.
- Zero contention: no reader/writer locks, no lock convoys, no deadlock surface, read cost is O(1) dictionary lookup.
- The snapshot **is** the resilience mechanism: when a refresh fails, simply not swapping leaves the last-known-good configuration serving reads (the case's storage-down requirement falls out of the design for free).

**Negative**
- Full snapshot rebuild per refresh — O(n) allocations each interval. Config sets are small (tens to hundreds of keys); irrelevant in practice.
- Reads can be one refresh-interval stale by design; the broker-triggered refresh (ADR 0005, shipped in Phase 5) reduces this to milliseconds.

## Alternatives Considered

- **`lock` around a shared dictionary** — correct but serializes the hot path and couples read latency to refresh duration (read-path contention).
- **`ReaderWriterLockSlim`** — better than `lock`, still blocks readers during swap and adds disposal/recursion pitfalls for no benefit at this size.
- **`ConcurrentDictionary` mutated in place** — thread-safe *per key operation* but not *per refresh*: while the loop upserts/removes entries, a reader can observe key A from the new generation and key B from the old one (the mixed-generation read problem). Per-key atomicity is the wrong unit; the consistency unit here is the whole config set.
- **MongoDB Change Streams instead of polling** — elegant push model, but the case mandates parametric polling (`refreshTimerIntervalInMs` is a required constructor parameter, and "periodic check" is a hard requirement). Change streams also require replica-set deployments. Push-based freshness is the EXTRA broker path (ADR 0005), layered *on top of* polling, never replacing it.
- **Per-call DB read + TTL cache** — no snapshot to manage, but the cache expiring during a storage outage leaves the reader with *nothing* to serve, violating the case's last-known-good requirement; it also puts I/O (and its latency spikes) back on the hot path whenever the TTL lapses.
