---
name: case-compliance
description: Use before any commit that touches DynamicConfig.Library's public surface, or on demand ("compliance check"). Verifies the implementation against the case PDF's hard requirements - constructor arity and parameter order, GetValue signature, query-level IsActive/ApplicationName filtering, .NET 8 target, storage-down fallback, polling interval, supported value types.
---

# Case Compliance Scan

Hard requirements from the case PDF (Turkish original: `Backend Developer Code Case.pdf`). Every item below is a **pass/fail gate** — report each with evidence (file:line), then an overall verdict. Any FAIL blocks the commit until fixed or explicitly waived by the user.

## Hard requirements

| # | Requirement | How to verify |
|---|---|---|
| 1 | Ctor is exactly `ConfigurationReader(string applicationName, string connectionString, int refreshTimerIntervalInMs)` — 3 params, this order, these names | Read `ConfigurationReader` public ctor. Internal/testing ctors are allowed; the **public** one must match exactly. |
| 2 | Exactly one public method with signature `T GetValue<T>(string key)` | Scan all `public` members of `ConfigurationReader`. No extra public methods creep in (`Dispose` from `IDisposable`/`IAsyncDisposable` is acceptable plumbing; flag anything else). |
| 3 | Only `IsActive = true` records are ever served | The storage query must filter `IsActive` — check `MongoConfigurationStorageProvider` filter definition, not in-memory `.Where()`. |
| 4 | Per-service isolation at the **query level** | Same query must filter `ApplicationName == <own>`. Grep the library for any code path that fetches records without the `applicationName` argument. |
| 5 | Library targets .NET 8 | `<TargetFramework>net8.0</TargetFramework>` in `DynamicConfig.Library.csproj`. |
| 6 | Storage unreachable → serve last successful snapshot | Refresh failure path must catch, log, and **not** clear/replace the current snapshot. There must be a unit test proving it (provider mock that throws after a successful load). |
| 7 | Polling honors `refreshTimerIntervalInMs` | The interval parameter must flow into the timer (`PeriodicTimer` period). No hardcoded interval anywhere. |
| 8 | Supported types: `string`, `int`, `double`, `bool` — conversion inside the library | Conversion engine handles all four; mismatch → `ConfigurationTypeMismatchException`; unknown key → `ConfigurationKeyNotFoundException`. Unit tests cover all four + mismatch + not-found. |
| 9 | Record schema is `Id, Name, Type, Value, IsActive, ApplicationName` | `ConfigurationRecord` model fields match (extra audit fields like `LastModifiedDate` are additive and fine). |

## Output format

```
CASE COMPLIANCE — <date>
 1. ctor signature ............ PASS (ConfigurationReader.cs:NN)
 2. GetValue surface ........... PASS (...)
 ...
VERDICT: PASS | FAIL (n items)
```
