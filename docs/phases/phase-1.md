# Phase 1 — Domain & Storage Layer

**Status:** done · **Completed:** 2026-07-02 · **Commit:** `feat(phase-1): domain model and mongo storage provider with query-level isolation` (single phase commit)

## Goal

Deliver the domain model and the storage layer beneath the future reader: the `ConfigurationRecord` schema required by the case, the `IConfigurationStorageProvider` seam (ADR 0003), and the MongoDB implementation whose `(ApplicationName, IsActive)` filtering happens at the query level so no foreign or inactive record ever enters the process.

## What was built

- **`Models/ConfigurationRecord`** — plain POCO with the case's six fields (`Id, Name, Type, Value, IsActive, ApplicationName`) plus additive `LastModifiedDate` (UTC) for cheap change detection on future polls. No MongoDB attributes: the model stays provider-agnostic.
- **`Models/ConfigurationValueType` + `ConfigurationValueTypes`** — enum of the four supported types and a tolerant parser for the record's free-text `Type` field (case-insensitive; accepts both the PDF sample's spellings `Int`/`bool` and the prose's `integer`/`boolean`; trims whitespace from manual UI entry).
- **`Storage/IConfigurationStorageProvider`** — the Strategy/Repository seam: exactly one method, `Task<IReadOnlyList<ConfigurationRecord>> GetActiveRecordsAsync(applicationName, cancellationToken)`. Phase 2's reader will depend on this, never on Mongo.
- **`Storage/Mongo/MongoConfigurationStorageProvider`** — driver 3.9 implementation:
  - isolation filter built as a Mongo query document (`{ ApplicationName, IsActive: true }`), never post-filtered in memory; a unit test renders the `FilterDefinition` and asserts the exact query BSON;
  - `(ApplicationName, IsActive)` compound index created idempotently on first use, guarded by a `volatile` flag + `SemaphoreSlim` (retries on failure instead of poisoning);
  - async/await end-to-end with `ConfigureAwait(false)`; `CancellationToken` flows through every I/O call; construction never touches the network (MongoClient connects lazily);
  - database name taken from the connection string when present, else the `DynamicConfigDb` default; all names live in `MongoConfigurationStorageDefaults` (no magic strings).
- **`Storage/Mongo/MongoConfigurationClassMap`** — code-based `BsonClassMap` (thread-safe `TryRegisterClassMap`): string `Id` persisted as ObjectId with server-side generation, `LastModifiedDate` round-trips as UTC by contract, extra document elements ignored for forward compatibility.
- **Tests** — 30 unit tests (xUnit), all green, no MongoDB required: type-name parsing matrix, BSON serialization/round-trip/UTC/extra-element behavior, rendered-filter assertion (the query-level isolation proof), database-name resolution, and argument guards.
- `DynamicConfig.Library.csproj`: `MongoDB.Driver 3.9.0`, XML docs generation (the dll is the deliverable), `InternalsVisibleTo` for the test project.

## Key decisions

None at ADR level — the phase implemented ADR 0001 (query-level isolation + compound index) and ADR 0003 (storage behind interface) as designed. Implementation-level choices, recorded here:

- **Code-based BsonClassMap over attributes** — keeps the domain model free of MongoDB dependencies, per the spirit of [ADR 0003](../adr/0003-storage-behind-interface.md).
- **Tolerant `Type` parsing** — the case PDF itself mixes spellings (`Int` in the sample table, `integer`/`boolean` in the prose); the parser accepts all case-insensitively so hand-entered records don't break consumers.
- **Index creation on first use, not in the constructor** — constructors can't `await`; a lazy, retrying bootstrap also keeps "storage down at startup" a survivable runtime concern (feeds ADR 0004 in Phase 3).
- **`InternalsVisibleTo` test seam** — lets tests exercise the filter builder and database-name resolution directly without widening the public surface the case locks down.

## Test coverage

`dotnet test` → 30/30 green, ~0.2 s, zero database dependency. Proven behavior:

- `Type` field parsing: all four supported types in every observed spelling; unknown/blank names rejected.
- BSON mapping: ObjectId `_id`, case-schema element names, full round-trip fidelity, UTC `DateTimeKind` on deserialization, unknown-extra-element tolerance.
- **Query-level isolation (case hard requirement):** the provider's filter renders to exactly `{ ApplicationName: <name>, IsActive: true }` — both conditions in the storage query document.
- Database-name resolution from connection string (named, with auth/options, absent → default).
- Argument guards: blank/null connection string and blank application name throw before any I/O.

Provider-against-real-Mongo verification is deliberately integration scope (docker-compose e2e, Phase 6 or the Phase 4 demo); mocking `IMongoCollection` internals was rejected as low-value.

## Deviations from plan

- **Compound index is created on first `GetActiveRecordsAsync` call, not "on startup"** — a constructor cannot be async, and eagerly connecting at construction would make storage-down-at-boot fatal. Creation remains idempotent (named index, Mongo `createIndexes` is a server-side no-op when it already exists) and retries on the next call after a failure.
- None otherwise — scope matched the phase plan exactly; no broker code, no reader code.
