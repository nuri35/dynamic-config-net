# Phase 4 — Web UI (REST + Frontend)

Phase 4 is delivered in three sub-phases, each with its own commit and quality gates:

| Sub-phase | Scope | Status |
|---|---|---|
| 4.1 | WebUI backend: admin repository & service layer | done |
| 4.2 | REST API: controllers/endpoints, DTOs, HTTP error mapping | done |
| 4.3 | Frontend: list/add/update UI, client-side name filter | done |

## Out of scope — deliberate: authentication/authorization

The case does not require authentication or authorization, so this admin surface ships without it — that is scope discipline under a 2-day deadline, not an oversight. In production, an admin UI that can change every service's runtime configuration would never be exposed bare: it would sit behind a reverse proxy with SSO, or use ASP.NET Core Identity / JWT bearer auth with an admin role. The service/repository split made in 4.1 keeps that retrofit cheap — auth is a concern for the (4.2) HTTP edge, and no business rule below it would change.

---

# Phase 4.1 — WebUI Backend: Admin Repository & Service Layer

**Status:** done · **Completed:** 2026-07-03 · **Commit:** `feat(phase-4.1): webui admin repository and service layer with write-side validation` (single phase commit)

## Goal

Give the WebUI its own backend: an admin data-access layer over the same Mongo collection the library reads, and a service layer owning every write-path business rule (validation, timestamp stamping, not-found semantics) so the Phase 4.2 controllers can stay thin. No controllers, no DTOs, no HTTP surface, no frontend in this sub-phase.

## What was built

- **`IConfigurationAdminRepository` + `MongoConfigurationAdminRepository`** (`src/DynamicConfig.WebUI/Storage/`) — the WebUI's own data-access contract: `GetAllAsync` (every application, inactive records included), `GetByIdAsync`, `CreateAsync`, `UpdateAsync`. Async end-to-end, `CancellationToken` flowing through every call, `ConfigureAwait(false)` on I/O. Reads/writes the exact collection the library polls, via the library's now-public `MongoConfigurationStorageDefaults` (names) and `MongoConfigurationClassMap` (BSON mapping). A malformed ObjectId is treated as not-found rather than a `FormatException`.
- **`IConfigurationAdminService` + `ConfigurationAdminService`** (`src/DynamicConfig.WebUI/Services/`) — business rules:
  - `Name` and `ApplicationName` required, non-blank.
  - Declared `Type` must be one of the four supported types (`ConfigurationValueTypes.TryParse` — same parser the readers use).
  - `Value` must be parseable as the declared `Type` (`Type=int, Value="abc"` → rejected) — via the library's `ConfigurationValueParser`.
  - `CreateAsync`/`UpdateAsync` stamp `LastModifiedDate = DateTime.UtcNow`; the service — not callers, not storage — owns that field.
  - `CreateAsync` takes the client's activity choice as a tri-state `bool? isActive` parameter; omitted (`null`) defaults to **active** (see key decisions).
  - `GetByIdAsync`/`UpdateAsync` validate the id *before any storage I/O*: blank or malformed ids throw `ConfigurationValidationException` instead of leaking a Mongo `FormatException` or masquerading as not-found. The format rule (`IsWellFormedId`) lives on the repository contract, because id shape is storage knowledge.
  - Update/read of a missing (well-formed) id → `ConfigurationRecordNotFoundException`; validation failure → `ConfigurationValidationException` (HTTP mapping lands in 4.2).
  - `ConfigurationValidationException` carries structured context: a `FieldName` property (set via `nameof`, so the 4.2 UI can attach errors to form fields) and a **private constructor with named static factories** (`RequiredFieldMissing`, `MalformedId`, `UnsupportedType`, `ValueTypeMismatch`) — every message template lives in exactly one place and a bare `new ConfigurationValidationException("invalid")` cannot compile. The supported-type list in the message is derived from the `ConfigurationValueType` enum, so it can never drift from what the library actually accepts.
- **`ConfigurationValueParser`** (library, `Conversion/`) — extracted the raw parse checks (invariant-culture int/double, 1/0 + true/false bool) out of `ConfigurationValueConverter` into a public class; the converter now delegates to it, adding only the read-path failure semantics (`ConfigurationValueFormatException`).
- **DI wiring** (`Program.cs`) — singleton `IMongoDatabase` (one `MongoClient` per process, per driver guidance), repository and service registrations, connection string from `ConnectionStrings:Mongo` with the same database-name fallback rule the library uses. No endpoints beyond a placeholder root.
- **Tests** — new `tests/DynamicConfig.WebUI.Tests` project (34 tests) with a hand-rolled `FakeConfigurationAdminRepository` (same idiom as the library tests), plus 27 new library tests for `ConfigurationValueParser`. 130 total, all green, zero database dependency.

## Key decisions

- **Admin repository vs. library provider — two bounded contexts, not one interface.** The library's `IConfigurationStorageProvider` is a *consumer* contract: one application's records, active only, read-only — that narrowing IS the case's isolation requirement. The WebUI needs an *admin* contract: every application, inactive included, read-write. Extending the provider would force admin capabilities onto every consuming service and dilute the isolation guarantee; instead the WebUI owns `IConfigurationAdminRepository`, and the two contexts meet only at the shared collection. Not raised to an ADR: it composes with ADR 0003 rather than revising it.
- **Single source of truth for type/value parsing.** The service validates `Value` against `Type` with the very code that parses on the read path (`ConfigurationValueTypes` + the extracted `ConfigurationValueParser`). Duplicating the tolerant parse rules in the WebUI would let "valid to write" drift from "parseable to read" — the exact bug class this phase exists to prevent.
- **Shared storage constants and BSON map instead of grep-verified duplicates.** `MongoConfigurationStorageDefaults` and `MongoConfigurationClassMap` went `internal` → `public` so the admin repository references them directly: a collection-name or Id-serialization mismatch (which would silently break the end-to-end story) is now impossible at compile time. The case-frozen public surface (`ConfigurationReader` ctor + `GetValue<T>`) and `IConfigurationStorageProvider` are untouched.
  - **The trade-off, stated plainly:** making `ConfigurationValueParser`, `MongoConfigurationStorageDefaults` and `MongoConfigurationClassMap` public *widens the library's public surface* beyond what the case strictly requires, and every public type is a compatibility promise — renaming these, changing the parse tolerances, or restructuring the class map is now a breaking change for any consumer that referenced them, not a free internal refactor. That lost freedom is the price paid for compile-time consistency between the two bounded contexts. For this project's scope — one library, one WebUI, one repository, one team — the consistency guarantee is worth strictly more than the flexibility; in a widely-distributed NuGet package the same call would deserve more hesitation (an `InternalsVisibleTo` or a shared-internals package would compete). Known, accepted, revisitable.
- **`LastModifiedDate` is owned by the service write path.** The library's duplicate resolution and change detection order records by `LastModifiedDate`; a stale timestamp on update would make consumers keep serving the old value. Stamping happens in `ConfigurationAdminService` on both create and update, in UTC, and is pinned by test.
- **Write-side prevention + read-side defense.** The service validation prevents the UI from ever writing a record whose `Value` can't be read as its `Type`; the library's `ConfigurationValueFormatException` remains the safety net for records that bypass the UI (manual Mongo edits, external tools). Complementary layers, not redundancy.
- **Repository returns `bool` on update; the service decides it means not-found.** Storage reports facts (`MatchedCount > 0` — matched, not modified: a no-change save is still a successful update); business meaning stays in the service layer.
- **`IsActive` defaults to `true` on create when the client doesn't provide it.** A record created without an explicit choice takes effect immediately — a config that is invisible-by-default surprises operators, and "add a value so services can read it" is the overwhelmingly common intent. Explicit `false` stays possible for staging a record ahead of activation. Mechanically, the choice travels as a `bool? isActive` parameter on `CreateAsync` (null = "not provided"), because the case-schema record's `IsActive` is a non-nullable `bool` and cannot express omission; the record's own field is ignored on create.
- **Id format is validated early, in the service, with storage owning the rule.** Blank/malformed ids are business-rule failures (`ConfigurationValidationException` → 400 in 4.2), not storage errors: without this, a garbage id either leaks a Mongo `FormatException` (500) or silently reads as not-found (misleading 404). The service can't hardcode "24-hex ObjectId" without coupling to Mongo, so the well-formedness question is asked through `IConfigurationAdminRepository.IsWellFormedId` — each storage implementation defines its own id shape. The repository's own `TryParse` guards remain as read-side defense for callers that bypass the service.

## Test coverage

`dotnet test` → 138/138 green (96 library + 42 WebUI), zero database dependency. New behavior proven:

- **Validation matrix (create):** all four types accepted with parseable values (including the case PDF's `"Int"` casing and `1`/`0` booleans); unsupported types (`decimal`, `date`, blank) rejected; corrupt values per type (`int/"abc"`, `int/"1.5"`, `double/"1,5"` tr-TR comma, `bool/"yes"`, `bool/"2"`) rejected with messages carrying both the value and the type; blank `Name`/`ApplicationName` rejected; an invalid record provably never reaches the repository.
- **Timestamp ownership:** create stamps `LastModifiedDate` with current UTC; update *refreshes* it even when the caller supplies a stale date (the load-bearing test), asserted on `DateTimeKind.Utc` and an in-range wall-clock window.
- **Not-found paths:** update with unknown id and get-by-id with unknown id both throw `ConfigurationRecordNotFoundException` carrying the id.
- **`IsActive` tri-state on create:** omitted → stored active (the default rule); explicit `false` → stored inactive; explicit `true` → stored active. The record's own `IsActive` field is provably ignored.
- **Id validation:** blank, non-hex and too-short ids on get-by-id/update throw `ConfigurationValidationException` before the repository is ever called (pinned by asserting no storage interaction); the fake repository delegates to the production `IsWellFormedObjectId` rule so tests exercise the exact Mongo semantics.
- **Admin breadth:** `GetAllAsync` returns inactive records and foreign applications — no consumer-style narrowing.
- **Repository filter logic:** `BuildIdFilter` renders as `{ _id: ObjectId(...) }` under the shared class map (a string-typed comparison would silently match nothing). Mongo CRUD plumbing is deliberately untested at unit level — mocking `IMongoCollection` internals is low value; end-to-end behavior is covered when the UI runs against the compose stack.
- **Parser extraction:** 27 tests pin `ConfigurationValueParser.IsParseableAs` per type (invariant culture, 1/0 booleans, null never parseable); the existing 69 library tests prove the converter refactor changed nothing on the read path.

## Deviations from plan

- **Shared constants instead of duplicated-and-grepped ones:** the plan suggested grep-verifying that WebUI and library collection/db constants agree; making the library's storage constants and class map public removes the duplication entirely, so the grep now shows a single constant referenced from both sides. Chosen because it turns a silent runtime failure mode into a compile-time impossibility; flagged for review in the phase summary.
- Everything else matched scope: no controllers/DTOs/HTTP, no frontend, no broker code, `IConfigurationStorageProvider` untouched.

---

# Phase 4.2 — REST API

**Status:** done · **Completed:** 2026-07-03 · **Commit:** `feat(phase-4.2): rest api with problemdetails error mapping and swagger` (single phase commit)

## Goal

The thin HTTP shell around the 4.1 service: four endpoints, DTOs that keep the entity off the wire, one central exception-to-HTTP mapping, and a browsable Swagger surface. Zero business logic — if a controller ever needs a business check, that is a 4.1 gap to fix in the service, never in the controller.

## What was built

- **`ConfigurationsController`** (`Controllers/`) — `GET /api/configurations` (all applications, inactive included — the admin view), `GET /api/configurations/{id}`, `POST /api/configurations` (201 + Location via `CreatedAtAction`), `PUT /api/configurations/{id}` (strict update; unknown id → 404, upsert deliberately rejected as decided in 4.1). Every action is 2–3 lines: bind → service call → wrap. No try/catch anywhere in the controller.
- **Contracts** (`Contracts/`) — `ConfigurationWriteRequest` (abstract shared shape: `Name`/`Type`/`Value`/`ApplicationName` with `[Required]`+`[NotBlank]`, `IsActive` as **nullable** bool), `CreateConfigurationRequest`/`UpdateConfigurationRequest` (mapping to a fresh record; update takes its id exclusively from the route), `ConfigurationResponse` (full read shape incl. server-owned `Id` and `LastModifiedDate`), and a small `NotBlankAttribute` (whitespace-only guard complementing `[Required]`).
- **`GlobalExceptionHandler`** (`ErrorHandling/`, .NET 8 `IExceptionHandler` — the NestJS exception-filter counterpart) — the single place exceptions become HTTP: `ConfigurationValidationException` → 400 with a `fieldName` extension, `ConfigurationRecordNotFoundException` → 404 with a `recordId` extension, everything else → 500 with a generic title and **no** message/type/stack. RFC 7807 ProblemDetails throughout; unexpected failures are error-logged with full detail (logs are trusted, response bodies are not).
- **Swagger via Swashbuckle** — all endpoints and DTO schemas documented; `GenerateDocumentationFile` + `IncludeXmlComments` wire the XML summaries into the UI; served at `/swagger` with `/` redirecting there.
- **Program.cs** — controllers, `AddProblemDetails` + `AddExceptionHandler`, Swagger, and the 4.1 DI registrations unchanged (connection string from `ConnectionStrings:Mongo`, database-name fallback through the library's public `MongoConfigurationStorageDefaults`).
- **Tests** — 23 new (161 total, all green): controller tests against a hand-rolled fake service, handler mapping tests, and contract shape tests.

## Key decisions

- **Thin-controller discipline as a hard rule.** Actions bind, call, wrap — nothing else. Error translation is middleware, business rules are the service. This is what makes the 4.1/4.2 split honest: the HTTP layer is replaceable (minimal APIs, gRPC, a console admin tool) without touching a single rule.
- **Two validation layers, zero overlap.** DataAnnotations reject *shape* garbage at the door (missing/blank required fields → automatic 400 with field detail, before model code runs); the service owns every *semantic* rule (supported Type, Value parseable as Type). The deep rules are deliberately **not** duplicated as attributes — one source of truth per rule, and a non-HTTP caller of the service still hits the full rule set.
- **Server-owned fields enforced by type, not documentation.** Request DTOs simply have no `Id` or `LastModifiedDate` properties — a client cannot send what cannot bind. The update id comes exclusively from the route. Pinned by a reflection test.
- **`IsActive` tri-state completed at the HTTP end.** The DTO's nullable bool is the wire end of 4.1's `bool? isActive` channel: omitted JSON property → `null` → service defaults to active. The controller forwards it untouched — the default lives in exactly one place (the service).
- **RFC 7807 ProblemDetails everywhere, extensions for machine-readable context.** `fieldName` (400) and `recordId` (404) ride as ProblemDetails extensions — this is what 4.1's exception `FieldName` property was built for; 4.3's form will attach errors to inputs with it. 500s leak nothing: generic title only.
- **Swagger enabled in every environment.** An internal admin tool the reviewer must be able to exercise from a container (which runs as Production by default); gating it on Development would hide the API exactly when it is being evaluated.

## Test coverage

`dotnet test` → 161/161 green (96 library + 65 WebUI), zero database dependency. Also smoke-tested over real HTTP: Swagger UI/JSON 200, malformed id → 400 ProblemDetails with `fieldName` through the actual middleware pipeline, missing `Name` → automatic 400 with field detail. New behavior proven:

- **Controller shell:** each action calls the right service method with the right arguments (route id → `GetByIdAsync`/record.Id, DTO fields → record); `GET` all returns inactive records (admin view); `POST` returns `CreatedAtAction` pointing at `GetById` with the server-generated id; omitted `IsActive` reaches the service as `null`, explicit `false` as `false`.
- **No-catch proof:** a service that throws not-found propagates uncaught out of `GetById`/`Update` — a swallowed exception would turn 404s into 200s.
- **Handler mapping:** 400 carries `Detail` + `fieldName`; 404 carries `Detail` + `recordId`; 500 contains neither the internal message nor the exception type anywhere in the serialized body; `TryHandleAsync` writes the matching status code and returns handled.
- **Contract shape:** valid request passes; blank `Name`/`ApplicationName`/`Type` fail with the exact member name; unknown-but-present type name *passes* the shape layer (the service's job — the zero-overlap rule, pinned); omitted `isActive` deserializes as `null`, explicit `false` as `false`; request types structurally lack `Id`/`LastModifiedDate`.

## Deviations from plan

None — no frontend code, no broker code, no business checks surfaced while writing the controller (the 4.1 service covered every path the shell needed).

# Phase 4.3 — Frontend

**Status:** done · **Completed:** 2026-07-03 · **Commit:** `feat(phase-4.3): vanilla js admin frontend with client-side name filter` (single phase commit)

## Goal

The last CORE step: a single-page admin UI served as static files from the WebUI project — list all records, client-side name filter, create and edit forms wired to the 4.2 API, field-level error display. Closing this reaches the ✅ CHECKPOINT: every mandatory case requirement is met.

## What was built

- **`wwwroot/index.html` + `app.js` + `style.css`** — no framework, no build pipeline, no npm. Served via `UseDefaultFiles`/`UseStaticFiles`; `/` now serves the UI (Swagger stays at `/swagger`, linked from the header).
- **Table view** — on load, one `GET /api/configurations`; renders every record (all applications, inactive included). Inactive rows are muted and badged. Columns: Name, Type, Value, Status, Application, Last modified (UTC), Edit.
- **Client-side name filter** — the compliance-sensitive item: the input's handler is `renderTable` only, which projects the already-loaded `allRecords` array (case-insensitive contains on Name). **No fetch exists anywhere on the filter path** — verified by the compliance scan.
- **Create** — "+ Add" opens an empty form; Type is a **dropdown** of the four supported types; IsActive is a checkbox defaulting to checked (unchecked posts `false` — the DTO's nullable channel stays intact). 201 → close, re-fetch, re-render.
- **Edit** — per-row button pre-fills the form from the in-memory record (stored type spellings like `Int`/`boolean` normalize onto the dropdown). The id is never a form field: it rides in `data-record-id` and travels only via the PUT route — server-owned discipline extended to the UI. 200 → close, re-fetch, re-render.
- **Error handling — the 4.1→4.2→4.3 payoff:** a 400's `fieldName` (service ProblemDetails) or `errors` map (automatic DataAnnotations 400) renders the message directly under the matching input; 404 on edit closes the form, shows an honest "record no longer exists" message and refreshes the list (consistent with the strict-update/no-upsert stance); network failures and 500s show one generic, non-technical message.

## Key decisions

- **Vanilla JS, stated plainly:** the case does not score visuals; a SPA framework would add a build pipeline, node_modules and docker complexity while covering zero additional requirements. Discipline is kept without the framework: one in-memory state (`allRecords`), small single-purpose functions (`fetchAllRecords` / `renderTable` / `openCreateForm` / `openEditForm` / `submitForm` / `showFieldError`), strict one-way flow (API → state → render). The DOM is a render target only — the edit form reads its record from state via closure, never back out of the DOM.
- **Type dropdown over free text** — a typo'd type is impossible by design; the service's `UnsupportedType` rejection remains as defense for non-UI clients.
- **XSS-safe rendering** — rows are built with `createElement`/`textContent`, never concatenated into `innerHTML`: record values are user data and must not be interpretable as markup.
- **No JS test framework — deliberate scope decision.** Every behavior guarantee (validation, tri-state, error shapes, status codes) already lives in the 165 API-side tests; the frontend is a thin projection of that API. A JS test harness (Jest/Vitest + DOM emulation) would re-test the same contracts at higher tooling cost. Verification instead: a real-HTTP smoke pass (documented below) plus code inspection for the zero-fetch filter guarantee.

## Write-side concurrency model (documented, deliberately not implemented)

Three known windows, all accepted for this project's scale — an internal admin tool distributing configuration:

1. **Concurrent admin edits — last-write-wins by design.** Two admins editing the same record race; the later `ReplaceOne` wins silently. The known alternative is optimistic concurrency (a version/ETag column, `409 Conflict` + reload flow) — deliberately out of scope here; it would be the right call in a finance-grade or multi-team context.
2. **Related-records window.** A poll can land between two related updates (e.g. a provider name and its API key saved seconds apart), so a consumer may run for one interval with a mixed pair; the next poll self-heals. The known remedies — config-set versioning or a batch-update endpoint applying both atomically — are out of scope because the case treats every record as independent.
3. **Cross-instance propagation window.** N service instances poll on unaligned timers, so after a write, instances converge within ≤1 poll interval each (eventual consistency — the standard model for config/feature-flag distribution). The Phase 5 broker, if approved, shrinks this to sub-second; polling remains the guaranteed-convergence fallback either way.

## Test coverage / smoke results

Frontend behavior is specified by the API tests (see the no-JS-test-framework decision); the suite ends the phase at 165/165 green. Real-HTTP smoke pass against the running app with MongoDB up:

- Page loads at `/`; static assets serve 200.
- Create round-trip: POST → 201 → record appears in the re-rendered table.
- Edit round-trip: PUT with the row's id → 200 → updated value renders.
- Field-level error: submitting `Type=int, Value=abc` → 400 → message rendered under the Value input (`fieldName` extension consumed).
- Filter: typing narrows the table with zero network requests (code inspection: the input handler is `renderTable`, which contains no `fetch`; `fetch` appears only in `fetchAllRecords` and `submitForm`).

## Deviations from plan

One addition beyond the planned scope: the smoke-caught update-path `IsActive` fix described above touched 4.1's service (`UpdateAsync` signature) and 4.2's controller (one argument). No broker code, no framework, no JS test tooling; the `/` → `/swagger` redirect from 4.2 was replaced by the UI as planned (Swagger remains reachable and linked).
