# ADR 0001: MongoDB as Configuration Storage

- **Status:** Accepted
- **Date:** 2026-07-02 (Phase 0; implemented in Phase 1)

## Context

The case allows any storage (MsSQL, Redis, Mongo, file...). Requirements that actually constrain the choice:

- Per-service isolation must be enforced **at the query level** — a service must never receive another service's records.
- Only `IsActive = true` records are served, so every read is a filtered read on `(ApplicationName, IsActive)`.
- `Value` is heterogeneous (string carrying int/double/bool/string, declared by a `Type` field) — there is no relational shape to exploit: one collection, no joins, no cross-record transactions.
- All I/O must be async end-to-end (bonus criterion), and using MongoDB/Redis is itself an extra-point item.

## Decision

MongoDB (`mongo:7`), one `configurations` collection, with a **compound index on `(ApplicationName, IsActive)`**. The provider's only query is `find { ApplicationName: <mine>, IsActive: true }` — isolation is a property of the query, not of in-memory filtering.

## Consequences

**Positive**
- The per-service read is an indexed point query — the cheapest operation the system performs, and the one it performs most.
- Isolation cannot be bypassed by an application-layer bug: foreign records never leave the database.
- Schemaless documents absorb the string-typed `Value` + `Type` discriminator naturally; adding a future type costs no migration.
- Official C# driver is async-native, matching the TPL bonus criterion.
- `LastModifiedDate` on documents gives cheap change detection if delta polling is ever needed.

**Negative**
- No relational integrity: uniqueness of `(ApplicationName, Name)` must be enforced by a unique index rather than a composite PK — acceptable, Mongo unique indexes cover it.
- Ops-wise heavier than a file provider for a demo — mitigated by docker-compose.

## Alternatives Considered

- **MsSQL / RDBMS** — adds schema migrations, relational ceremony, and a sync-first mindset for a single-table workload with no joins or transactions. Buys nothing here.
- **Redis** — fine as a cache, but weak secondary querying (isolation filter would drift into key-naming conventions), and less natural as the durable system-of-record behind a management UI.
- **File** — trivially simple but fails the spirit of the case (shared, concurrently updated store for many services) and earns no bonus.
