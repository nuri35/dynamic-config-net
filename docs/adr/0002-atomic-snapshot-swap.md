# ADR 0002: Atomic Immutable-Snapshot Swap for Concurrency

- **Status:** Accepted
- **Date:** 2026-07-02 (Phase 0; implemented in Phases 2–3)

## Context

`GetValue<T>` is called from arbitrary application threads on the hot path, while a background loop periodically replaces the entire config set. "Preventing possible concurrency problems" is an explicit extra-point criterion. In multi-threaded .NET — unlike Node.js's single-threaded event loop, where this safety comes for free — concurrent read/write over shared mutable state must be engineered deliberately.

## Decision

The current configuration lives in an **immutable dictionary snapshot** referenced by a single field.

- **Read path:** `GetValue<T>` performs a volatile read of the snapshot reference and a dictionary lookup. No locks, no I/O, no awaiting.
- **Write path:** the refresh loop builds a *complete new* immutable snapshot off to the side, then publishes it with one atomic reference swap (`Interlocked.Exchange` / `volatile` write). There is exactly one writer (the refresh loop), many readers.

## Consequences

**Positive**
- Readers see either the entire old snapshot or the entire new one — a torn, half-updated view is structurally impossible.
- Zero contention: no reader/writer locks, no lock convoys, no deadlock surface, read cost is O(1) dictionary lookup.
- The snapshot **is** the resilience mechanism: when a refresh fails, simply not swapping leaves the last-known-good configuration serving reads (the case's storage-down requirement falls out of the design for free).

**Negative**
- Full snapshot rebuild per refresh — O(n) allocations each interval. Config sets are small (tens to hundreds of keys); irrelevant in practice.
- Reads can be one refresh-interval stale by design; mitigated by broker-triggered refresh (ADR 0003).

## Alternatives Considered

- **`lock` around a shared dictionary** — correct but serializes the hot path and couples read latency to refresh duration.
- **`ReaderWriterLockSlim`** — better than `lock`, still blocks readers during swap and adds disposal/recursion pitfalls for no benefit at this size.
- **`ConcurrentDictionary` mutated in place** — thread-safe per operation but *not* per refresh: readers could observe a mix of old and new values mid-update (torn state), which is exactly the bug class this ADR eliminates.
