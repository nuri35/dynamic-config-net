# Phase 2 — Core Reader

**Status:** done · **Completed:** 2026-07-02 · **Commit:** `feat(phase-2): configuration reader with snapshot cache and strict conversion engine` (single phase commit)

## Goal

Deliver the library's public face: `ConfigurationReader` with the case's exact frozen surface (3-param ctor + `T GetValue<T>(string key)`), serving typed values from an immutable in-memory snapshot loaded once at construction. No timer, no polling — refresh is Phase 3; this phase chooses the structures Phase 3 swaps.

## What was built

- **`ConfigurationReader`** — public ctor `(applicationName, connectionString, refreshTimerIntervalInMs)` exactly as the case specifies; wires `MongoConfigurationStorageProvider` internally, validates all three parameters with guard clauses, performs the initial snapshot load, stores the interval for Phase 3 (verified by test via an internal property). Public surface: the ctor and `GetValue<T>` — nothing else.
- **Internal DI constructor** — `(applicationName, IConfigurationStorageProvider, refreshTimerIntervalInMs)`, reachable through `InternalsVisibleTo`.
- **`Snapshot/ConfigurationSnapshot`** — immutable point-in-time view keyed by record `Name`, wrapping a **`FrozenDictionary`**; owns the normalization policy (blank names dropped, duplicates resolved). Includes `Empty` — unused this phase, deliberately staged as the ADR 0004 fallback seed.
- **`Conversion/ConfigurationValueConverter`** — strict declared-vs-requested type check, then per-type parsing: int/double via `CultureInfo.InvariantCulture`; bool accepts `1`/`0` and `true`/`false` case-insensitively.
- **`Exceptions/`** — `DynamicConfigurationException` (abstract base) with three concrete types: `ConfigurationKeyNotFoundException` (key + applicationName in message), `ConfigurationTypeMismatchException` (key + declared + requested), `ConfigurationValueFormatException` (key + declared type + raw value).
- **Tests** — 29 new (59 total, all green, zero MongoDB): `FakeConfigurationStorageProvider` (hand-rolled, captures the requested applicationName), construction/validation suite, full `GetValue` behavior suite.

## Key decisions

None at ADR level — the dual-ctor seam is exactly what [ADR 0003](../adr/0003-storage-behind-interface.md) prescribed, and the snapshot structure implements [ADR 0002](../adr/0002-atomic-snapshot-swap.md). Phase-level decisions, each with its WHY:

- **Dual constructor (public case-exact + internal DI)** — the case freezes the public ctor at three parameters, so dependency injection cannot enter through the public surface; the internal ctor is the DI door, exposed to tests via `InternalsVisibleTo`. The public ctor delegates to it, so both run identical code.
- **`FrozenDictionary` as the snapshot structure** (.NET 8) — build-once/read-many matches the snapshot lifecycle exactly: it trades one-time build cost for the fastest reads, and Phase 3's atomic swap only ever replaces the wrapping `ConfigurationSnapshot` reference. Chosen now so the swap lands without rework.
- **One-time sync-over-async bridge in the constructor** — the case's ctor is synchronous but the initial load is I/O. `Task.Run(...).GetAwaiter().GetResult()` confined to `LoadInitialSnapshot`: `Task.Run` prevents SynchronizationContext deadlocks, `GetAwaiter().GetResult()` rethrows the storage exception unwrapped. The ban on sync-over-async targets the request/refresh paths — `GetValue` and the Phase 3 loop never block.
- **Strict type matching, no widening** — `GetValue<double>` on an int-typed record throws `ConfigurationTypeMismatchException`. The declared `Type` is the record's contract; silent widening would hide authoring mistakes in the UI (a value meant to be fractional must be authored as double). One exception type covers mismatch, unsupported declared type, and unsupported requested `T` — the message always carries both sides.
- **Corrupt value ≠ type mismatch** — Type=int, Value="abc" throws a third exception, `ConfigurationValueFormatException`, so operators know the fix is editing the record, not the calling code. A raw `FormatException` never escapes.
- **Case-insensitive key lookup** — mirrors .NET's own `IConfiguration` semantics and forgives hand-entered records.
- **Duplicate active names: latest `LastModifiedDate` wins** — a management-UI double entry degrades to one deterministic winner instead of crashing every consumer.
- **Initial-load failure propagates (for now)** — a reader that never loaded has nothing to serve; whether this becomes empty-snapshot-plus-background-retry is exactly ADR 0004's question, decided in Phase 3. A test pins the current behavior so the Phase 3 change is visible.

## Test coverage

`dotnet test` → 59/59 green (~0.1 s), zero database dependency. New behavior proven:

- Happy path for all four supported types; bool accepts `1`/`0`/`true`/`TRUE`/`False`; double parses `"1.5"` correctly **with `CurrentCulture` forced to `tr-TR`** (invariant-culture proof).
- Unknown key → `ConfigurationKeyNotFoundException` with key and applicationName in the message; blank key → `ArgumentException`.
- Declared/requested mismatch, no-widening (int record read as double), unsupported declared type (`date`), unsupported requested type (`DateTime`) → `ConfigurationTypeMismatchException` with full context.
- Corrupt value (int/`"abc"`) → `ConfigurationValueFormatException` with key, type, and raw value.
- Case-insensitive lookup; duplicate names resolved by latest `LastModifiedDate`.
- Ctor validation (blank applicationName/connectionString, non-positive interval, null provider) — all thrown before any I/O; initial load happens exactly once and passes the exact applicationName to storage; interval stored; initial-load failure propagates unwrapped.

## Deviations from plan

- **Public-ctor happy path is not unit-testable** — it constructs a real Mongo provider, so its end-to-end proof is integration scope (Phase 4 demo / Phase 6 e2e). It delegates to the internal ctor, which the whole suite exercises; only the one-line provider wiring is uncovered.
- None otherwise — no timer, no polling, no broker code.
