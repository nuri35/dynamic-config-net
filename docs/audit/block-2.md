# Audit Block 2 — WebUI (Phases 4.1–4.3) · 2026-07-04

Same protocol as [Block 1](block-1.md). Verdict shorthand: ✓ = verified as claimed; FIXED = defect/gap corrected in this audit; NOTE = documented behavior/finding, no code change. Live probes ran against the compose Mongo (seeded dataset) on `http://localhost:5199`; all audit records (`ApplicationName: AUDIT-2`) were removed afterwards — the DB is back to the 6 seeded demo records.

## PART A — Requirements vs code

| # | Item | Evidence | Verdict |
|---|---|---|---|
| 1 | List/add/update work; NO delete | Live: `GET` 200 (6 records), `POST` 201 with body+Location, `PUT` 200 with refreshed body. Zero `[HttpDelete]` in `src/`; absence is noted as deliberate in phase-4.md (§smoke A5: "CORE has no DELETE endpoint (deliberate scope)") — the case asks for list/add/update only | ✓ |
| 2 | Name filter: zero fetch | Structural: `app.js` has exactly two `fetch` call sites (`fetchAllRecords`, `submitForm`); the filter input's only handler is `renderTable`, which projects the in-memory `allRecords` — no network path exists from the filter. Browser proof already on record from the 4.3/5.3 smokes | ✓ |
| 3 | Admin sees ALL apps + inactive | `MongoConfigurationAdminRepository.GetAllAsync` uses `Filter.Empty` (no ApplicationName/IsActive narrowing — that's the library's consumer contract). Live `GET` returned SERVICE-A **and** SERVICE-B records including two `isActive:false` rows | ✓ |

## PART B — Claim vs code

| # | Claim | Evidence | Verdict |
|---|---|---|---|
| 4 | "Two-layer zero-overlap validation" | Rule-by-rule map — DTO attributes: Name/Type/ApplicationName `[Required]+[NotBlank]`, Value `[Required]`. Service: Name/ApplicationName blank, Id well-formed, Type ∈ supported set, Value parseable as Type. **No deep rule exists at the door** (pinned: `BlankType_…_ButUnknownTypeNamePasses`), which is the banned overlap direction per phase-4.md's own wording. The service *does* re-check Name/ApplicationName blankness — deliberate superset so a non-HTTP caller hits the full rule set (stated in phase-4.md §"Two validation layers"). One asymmetry, defined: `Value: ""` is rejected at the door (`[Required]`, Swagger `minLength: 1`) though the library accepts `""` for `string` — at the HTTP boundary an empty JSON string is indistinguishable from an omitted field, so rejecting is the honest reading; whitespace `" "` passes and stores verbatim (see 15) | ✓ (claim holds in its precise sense) NOTE on `Value:""` |
| 5 | Thin controller | `ConfigurationsController`: every action is bind → one service call → wrap (`Ok`/`CreatedAtAction`). Zero `if`/`switch`/`try`/`catch` in the file | ✓ |
| 6 | LastModifiedDate refresh pin | `UpdateAsync_RefreshesLastModifiedDateWithCurrentUtc` green in this run (211/211) | ✓ |
| 7 | fieldName for EVERY factory | Structural: private ctor forces all instances through 4 factories (`RequiredFieldMissing`→Name/ApplicationName/Id, `MalformedId`→Id, `UnsupportedType`→Type, `ValueTypeMismatch`→Value); handler maps `validation.FieldName` unconditionally. Test gap: only `UnsupportedType` was pinned → added `Map_EveryValidationFactory_Produces400CarryingItsFieldName` (4-case theory) | FIXED (test gap) |

## PART C — Edge hunt (live API probes + 3 service pins added in `ConfigurationAdminServiceTests`)

| # | Edge | Outcome |
|---|---|---|
| 8 | Duplicate Name+ApplicationName | Two `POST`s both 201, both stored (`first`/`second` visible in `GET`). No unique index, no service check — allowed by design; the library resolves downstream (latest `LastModifiedDate` wins, pinned there). Behavior now pinned write-side: `CreateAsync_DuplicateNameAndApplication_IsAllowedByDesign` ✓ |
| 9 | 100 KB Value | `POST` → 201, no 500. No size rule exists — defined outcome is acceptance (Mongo's 16 MB doc limit and Kestrel's ~30 MB body limit are the only ceilings). Pinned: `CreateAsync_OversizedValue_IsAcceptedNotRejected` ✓ |
| 10 | `type: "banana"` via raw API | 400 with `detail: "Type 'banana' is not supported. Supported types: string, int, double, bool."` and `fieldName: "Type"` — the door passes unknown type names on purpose; the service rejects. Never a 500 ✓ |
| 11 | Foreign well-formed ObjectId | `GET /65f1a2b3c4d5e6f7a8b9c0d1` → clean 404 ProblemDetails with `recordId` extension ✓ |
| 12 | 20 concurrent PUTs, same record | 20× HTTP 200, zero 500s; final document is one coherent whole-payload replace (`ReplaceOneAsync` is atomic per document — no field interleaving possible); `lastModifiedDate` advanced past the prior write (each request stamps `UtcNow` pre-replace) ✓ |
| 13 | Empty database | Structural: `Find(Filter.Empty).ToListAsync` → empty list → `Ok([])`; `app.js` sets `emptyMessage.hidden = visibleRecords.length > 0` → empty state renders. (DB is seeded on this machine; structural check per brief) ✓ |

## PART D — Company-eye probes

| # | Probe | Finding |
|---|---|---|
| 14 | Response leaks Mongo internals? | Live JSON: exactly the 7 documented camelCase fields; `id` is a plain string (class map's `StringObjectIdGenerator` — no `{"$oid":…}` shape); no `_id`, no extras. `ConfigurationResponse` is the only serialized type; entity never crosses the boundary (pinned: write requests structurally lack `Id`/`LastModifiedDate`) ✓ |
| 15 | Trimming policy | DEFINED and now pinned: no trimming anywhere on the write path — `" AuditPadded "` / `" v "` stored verbatim (live probe + `CreateAsync_PaddedNameAndValue_AreStoredVerbatim`). `[NotBlank]` stops whitespace-*only* input; padding is the author's data. Consistent with the read side: the library looks up by exact stored name and its parser itself accepts padded numerics (Block 1 §12) |
| 16 | Swagger vs actual DTOs | `CreateConfigurationRequest` schema: `isActive` visible as **nullable boolean**, `required` = exactly the four `[Required]` fields, `minLength: 1` mirrors Required's empty-string rejection, no `id`/`lastModifiedDate` properties. `ConfigurationResponse` schema lists all 7 fields incl. `date-time` format. One blemish FIXED: the `value` description rendered a raw CLR path (`DynamicConfig.WebUI.Contracts.ConfigurationWriteRequest.Type`) because `<see cref>` doesn't render in Swagger → doc comment now uses `<c>Type</c>` |

## Closure

- **Zero code defects.** Three fixes, all doc/test-side: one handler-test gap (B7), three behavior pins for previously undefined-on-paper write behaviors (C8/C9/D15), one Swagger doc-comment rendering wart (D16).
- Block-1's three scope notes now live in the README as **"Deliberate Non-Goals"**.
- 211/211 tests green (129 library + 82 WebUI); frozen library surface untouched.
