# Phase 3 — Refresh & Resilience

**Status:** done · **Completed:** 2026-07-02 · **Commit:** `feat(phase-3): periodic refresh with atomic snapshot swap and storage-down fallback` (single phase commit)

## Goal

Make the reader live: a `PeriodicTimer`-based background loop re-queries storage every `refreshTimerIntervalInMs`, builds a fresh immutable snapshot and publishes it with an atomic reference swap (ADR 0002). A failed poll changes nothing — the current snapshot **is** the fallback. ADR 0004 (initial-load failure) is decided and written as Accepted.

## What was built

- **Polling loop** (`ConfigurationReader.RunRefreshLoopAsync`) — `while (await timer.WaitForNextTickAsync(token))` → `RefreshSnapshotAsync()`. Started in the ctor *after* a successful initial load; async end-to-end with `ConfigureAwait(false)`; the loop task is stored, never fire-and-forget, and structurally cannot fault.
- **Atomic swap** — `GetValue` copies the snapshot reference to a local with `Volatile.Read` once at entry and never re-reads the field; the refresh path publishes with `Volatile.Write` only after the new `FrozenDictionary`-backed snapshot is fully built. One writer (ticks can't overlap), many lock-free readers.
- **Resilience** — `RefreshSnapshotAsync` catches every non-cancellation exception, logs via `System.Diagnostics.Trace` and leaves the snapshot untouched; recovery is automatic on the first successful poll. No retry queue, no circuit breaker — deliberately.
- **Lifecycle** — `IDisposable` + `IAsyncDisposable`: `Dispose()` cancels the loop and disposes the timer without blocking; `DisposeAsync()` additionally awaits the loop task and disposes the `CancellationTokenSource`. Idempotent (`Interlocked.Exchange` guard); `GetValue` after disposal keeps serving the last snapshot (immutable data stays valid for a tearing-down host).
- **ADR 0004** written as **Accepted** (fail-fast); **ADR 0002** updated with the implemented `Volatile.Read`/`Write` mechanics and the full rejected-alternatives list.
- **Tests** — 10 new (69 total, all green): upgraded `FakeConfigurationStorageProvider` (mutable records, toggleable outage, `TaskCompletionSource`-based call signals — no sleep-polling), refresh semantics driven deterministically through the internal `RefreshSnapshotAsync`, loop/dispose proven against the real timer at a 10 ms interval.

## Key decisions

- **[ADR 0004](../adr/0004-fail-fast-initial-load.md) — fail-fast on initial load (Accepted).** The case's "last *successful* records" clause presupposes one successful load; an empty-snapshot service throws on every `GetValue` (an up-but-misbehaving zombie); a boot-time throw composes with orchestrator restart policies instead of re-implementing retry inside the library. Fail-soft documented as the rejected alternative. Pinned by test.
- **[ADR 0002](../adr/0002-atomic-snapshot-swap.md) updated** — alternatives now record all four rejections with reasons: locked dictionary (read-path contention), `ConcurrentDictionary` (per-key atomicity ≠ per-refresh consistency — the mixed-generation read problem), Mongo Change Streams (case mandates parametric polling; replica-set requirement), per-call DB + TTL cache (TTL expiry during an outage violates last-known-good).
- **`PeriodicTimer` over `System.Timers.Timer`/`System.Threading.Timer`** — (a) await-based API means no `async void` callback that swallows exceptions; (b) ticks are handed out one at a time, so a poll slower than the interval delays the next tick instead of racing it — overlap is eliminated by design, not by locking. Rationale lives as a comment at the field.
- **`Trace` for failure logging** — the case-frozen ctor leaves no DI door for `ILogger`; `System.Diagnostics.Trace` is the vendor-neutral channel a host can attach listeners to.
- **Disposal split** — sync `Dispose` must not block on the loop (project rule: the ctor's initial load is the only sanctioned sync-over-async bridge), so it cancels and returns; `DisposeAsync` is the complete teardown. The CTS is disposed only after the loop is awaited.

## Test coverage

`dotnet test` → 69/69 green (~0.7 s), zero database dependency. New behavior proven:

- Poll picks up a **new record**, a **changed value**, and a **deactivated record** (key disappears — IsActive filtering already happened at the query, the reader drops what stops arriving).
- **Storage down:** a throwing poll neither throws out of the refresh path nor clears the snapshot — the last good value keeps serving. **Recovery:** the first successful poll after an outage swaps in fresh data.
- **Consistency under concurrency:** 4 reader threads hammer two keys while snapshots flip between two generations for 500 ms — every read observes a complete generation, never a mixed one.
- **Loop liveness:** with a 10 ms interval, a record changed after construction becomes visible without any manual trigger (signal-based wait on the provider's call count).
- **Disposal:** `DisposeAsync` stops polling (call count provably frozen afterwards); double-`DisposeAsync` + trailing `Dispose` are safe; `GetValue` after disposal serves the last snapshot.
- **ADR 0004 pinned:** initial-load failure still propagates unwrapped (test comment now cites the accepted ADR).

## Deviations from plan

None — scope matched exactly: no broker code, no Web UI code, the ctor's sync bridge untouched, and the sync `Dispose` path deliberately does not await the loop (documented above) to honor the no-new-sync-bridges rule.
