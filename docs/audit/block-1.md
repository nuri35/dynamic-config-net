# Audit Block 1 — Library Core (Phases 0–3) · 2026-07-04

Verification-and-hunt pass. Verdict shorthand: ✓ = verified as claimed; FIXED = defect/gap corrected in this audit; NOTE = documented behavior/finding, no code change.

## PART A — Requirements vs code

| # | Item | Evidence | Verdict |
|---|---|---|---|
| 1 | Frozen ctor, character-exact | `ConfigurationReader.cs:62` — exactly `public ConfigurationReader(string applicationName, string connectionString, int refreshTimerIntervalInMs)`; the 4-param ctor is `internal` (test seam, not public surface) | ✓ |
| 2 | `GetValue<T>` only public method | Public members of `ConfigurationReader`: ctor, `GetValue<T>`, `Dispose`, `DisposeAsync` (interface plumbing). Full public surface: 15 types — reader, record model, `ConfigurationValueType(+s)`, `ConfigurationValueParser`, 4 exceptions, `IConfigurationStorageProvider` + Mongo provider/defaults/class-map, `RabbitMqBrokerDefaults`, `ConfigurationChangedEvent`. Every type beyond the case surface is a documented shared-kernel exception (4.1/5.2 trade-off notes) | ✓ |
| 3 | Only `IsActive=true` served | Single query path in the library (`MongoConfigurationStorageProvider.cs:56` `Find(BuildActiveRecordsFilter)`); rendered-filter test pins `{ApplicationName, IsActive:true}` server-side; no other `Find/ToListAsync` exists | ✓ |
| 4 | Query-level app isolation | Same single path; no unfiltered fetch anywhere | ✓ |
| 5 | 4 types + PDF spellings | `ConfigurationValueTypesTests` pins `Int`/`string`/`bool`/`boolean`/`integer` case-insensitively; conversion tests cover all four | ✓ |
| 6 | Parametric polling only | Sole interval source: `new PeriodicTimer(TimeSpan.FromMilliseconds(refreshTimerIntervalInMs))` — no other literal interval in the library | ✓ |
| 7 | Storage-down → last-good | `Refresh_StorageDown_KeepsServingLastGoodSnapshot` + recovery test, green | ✓ |

## PART B — Claim vs code

| # | Claim | Evidence | Verdict |
|---|---|---|---|
| 8 | One sync bridge (ctor) | Sweep: the only `.GetAwaiter().GetResult()` is `LoadInitialSnapshot`; no `.Result`/`.Wait()` anywhere | ✓ |
| 9 | Lock-free read | `GetValue`: one `Volatile.Read`, no lock/semaphore on the path (only `Interlocked` in `Dispose`) | ✓ |
| 10 | No tick overlap; failed poll = no-op | Phase 3 pins green (`PeriodicTimer` one-tick-at-a-time; throwing-provider tests) | ✓ |
| 11 | Fail-fast + empty-is-valid-boot | Fail-fast pinned (`Constructor_InitialLoadFails_Propagates…`); empty-boot pin was MISSING → added (`EmptyRecordSet_BootsFine_UnknownKeyThrowsKeyNotFound`) | FIXED (test gap) |

## PART C — Edge hunt (17 new pins in `AuditBlock1EdgeTests`, all green on first run — zero code defects)

| # | Edge | Outcome |
|---|---|---|
| 12 | int overflow `"99999999999999"` | `ConfigurationValueFormatException` — no `OverflowException` leak ✓ |
| 12 | double `"NaN"`/`"Infinity"` | Parse to IEEE `NaN`/`+∞` (NumberStyles.Float) — defined, now documented ✓ |
| 12 | `" 50 "` int / `""` per type / `TRUE`/`True` bool | Whitespace parses; empty string valid only for `string`, format-exception otherwise; bool casing-insensitive ✓ |
| 13 | `GetValue<decimal/DateTime/custom>` | `ConfigurationTypeMismatchException`, no cast crash ✓ |
| 14 | null/empty/whitespace key | `ArgumentNullException`/`ArgumentException` — never disguised as key-not-found ✓ |
| 15 | Two readers (A+B) in one process | Fully independent snapshots; B's refresh never leaks into A (no shared static state) ✓ |
| 16 | Reader never disposed | NOTE — behavior stated honestly: the loop task + `PeriodicTimer` + (if opted-in) AMQP connection stay rooted for the process lifetime; no finalizer by design. Background polling dies with the process; an undisposed reader in a long-lived host keeps polling — which is exactly its contract. Disposal is the host's job (`await using`), as in every `IHostedService`-style resource |
| 17 | Double-dispose / GetValue-after-dispose | Existing Phase 3 pins green ✓ |
| 18 | 1 ms interval / `int.MaxValue` interval | 1 ms: 5+ ticks served one-at-a-time, clean dispose; `int.MaxValue` ms (~24.8 days) constructs fine — within `PeriodicTimer`'s ~uint32 ms ceiling, no overflow ✓ |

## PART D — Company-eye probes (findings only)

| # | Probe | Finding |
|---|---|---|
| 19 | "web, wcf, web api her türlü proje" portability | Library targets `net8.0` only; deps are exactly `MongoDB.Driver` + `RabbitMQ.Client` — zero ASP.NET/hosting references, dependency direction clean (no WebUI/Demo refs). Any .NET 8 process type (console proven by DemoService, web by WebUI) can host it. Classic .NET Framework/WCF would need `netstandard2.0` multi-targeting — **scope finding, not implemented** (see below) |
| 20 | Partially-constructed escape | Ordering airtight: `_snapshot` is assigned (initial load) before the timer/loop start, and every field the consumer callback touches is assigned before `_consumerStart` is created; `this` never escapes the ctor before full construction. `GetValue` before the ctor returns is impossible for a correctly published reference (and the reference publish itself is the caller's memory-model concern, as with any .NET object) |
| 21 | NuGet-readiness | XML docs: 4 exception ctors were undocumented (CS1591) → FIXED, now zero warnings with `GenerateDocumentationFile` on. No packaging metadata (PackageId/version/license) — **scope finding** |

## Scope findings for the user (nothing implemented)

1. **netstandard2.0 multi-target** for classic .NET Framework/WCF consumers. Proposed note: *"Deliberate non-goal: the case mandates .NET 8; classic-Framework consumers would require netstandard2.0 multi-targeting and a driver downgrade audit."*
2. **NuGet packaging metadata** (PackageId, version, license, repo URL). Proposed note: *"Deliberate non-goal: the deliverable is a project-referenced dll per the case; packaging is a distribution concern outside the brief."*
3. **Undisposed-reader lifecycle** (C16): documented above as the honest behavior statement; a finalizer/`SafeHandle` pattern is not warranted for managed-only resources.

**Result: zero code defects. Two fixes: one missing test pin (B11), four missing XML doc comments (D21). 204/204 tests green; frozen surface character-identical.**
