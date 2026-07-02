# ADR 0004: Storage Behind `IConfigurationStorageProvider`

- **Status:** Accepted
- **Date:** 2026-07-02 (Phase 0; implemented in Phase 1)

## Context

The case demands TDD and unit tests (extra points), and lists many acceptable storages (MsSQL, Redis, Mongo, file...). The core reader logic — type conversion, snapshot management, refresh, resilience — is storage-agnostic by nature and must be testable without any database running. Design & architectural pattern usage is itself an extra-point item.

## Decision

`ConfigurationReader` depends only on the **`IConfigurationStorageProvider`** interface (Strategy + Repository pattern): a minimal async surface for "fetch the active records for this application". `MongoConfigurationStorageProvider` is the shipped implementation. The reader's public ctor (fixed by the case at exactly 3 parameters) constructs the Mongo provider from the connection string internally; an internal/testing seam injects a mock provider for unit tests.

## Consequences

**Positive**
- The entire core library is unit-tested against a mocked provider — conversion, isolation, refresh, key-not-found, and the storage-down fallback (a mock that throws) — with zero database dependency, keeping tests fast and deterministic.
- Swapping Mongo for Redis/SQL/file is one new class implementing one small interface; reader, conversion engine, and refresh loop are untouched (open/closed).
- The interface boundary is where the isolation contract lives: its single method takes `applicationName` and returns only active records, so *every* implementation is forced into query-level filtering semantics.

**Negative**
- One extra abstraction for a system with a single production implementation — justified here by the testing requirement alone.
- The 3-param ctor constraint means provider selection is internal (no public DI seam); the internal ctor keeps the public surface exactly as the case specifies while remaining testable.

## Alternatives Considered

- **Reader talks to the Mongo driver directly** — simplest, but unit tests would need a live Mongo (integration tests in disguise) and the storage bonus flexibility dies.
- **Full DI with public provider injection** — cleaner in a normal library, but violates the case's hard "exactly three parameters" ctor requirement; internal seam is the compromise.
