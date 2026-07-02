# Phase 0 — Scaffold & Workflow

**Status:** done · **Completed:** 2026-07-02 · **Commits:** `e427ac1` (scaffold), `780fe6d` (docs workflow), + core-first restructure commit

## Goal

Stand up the full skeleton so every later phase only adds behavior: solution + projects that build clean, infrastructure compose skeleton, and the documentation/AI-workflow layer (constitution, ADRs, skills, hook) as a first-class deliverable.

## What was built

- `DynamicConfig.sln` with four projects: `DynamicConfig.Library` (the deliverable classlib), `DynamicConfig.WebUI` (empty ASP.NET Core), `DynamicConfig.DemoService` (empty ASP.NET Core), `DynamicConfig.Library.Tests` (xUnit); references wired (WebUI/DemoService/Tests → Library). `dotnet build`: 0 warnings, 0 errors; `dotnet test`: 1/1 (placeholder).
- `docker-compose.yml` skeleton: `mongo:7` with healthcheck (**Mongo only** — RabbitMQ and app services are EXTRA, Phases 5–6).
- Repo hygiene: `.gitignore`, `.editorconfig` (file-scoped namespaces, `_camelCase` private fields, `I`-prefixed interfaces).
- Documentation layer: `CLAUDE.md` constitution (CORE/EXTRA phase split), `docs/architecture.md` (system diagram, data flows, failure modes), ADRs 0001–0003 accepted + 0005 drafted as proposed.
- AI workflow: `.claude/skills/` (`phase-docs`, `case-compliance`, `dotnet-reviewer`) + PostToolUse hook running `dotnet build` + library tests after every `.cs` edit (verified working).
- `README.md` written as finished-project presentation (architecture, design decisions with ADR links, usage, requirements coverage).

## Key decisions

The three CORE foundation decisions were ratified this phase (made in planning, documented here):

- [ADR 0001](../adr/0001-mongodb-as-storage.md) — MongoDB as storage, isolation enforced at query level via `(ApplicationName, IsActive)` compound index
- [ADR 0002](../adr/0002-atomic-snapshot-swap.md) — atomic immutable-snapshot swap; lock-free reads; snapshot doubles as the storage-down fallback
- [ADR 0003](../adr/0003-storage-behind-interface.md) — `IConfigurationStorageProvider` abstraction; internal ctor seam reconciles testability with the case's 3-param public ctor

Deferred decisions:
- First-load-failure behavior → ADR 0004 in Phase 3 (current lean: empty snapshot + background retry).
- [ADR 0005](../adr/0005-polling-plus-broker-hybrid.md) — polling + RabbitMQ hybrid, drafted as **proposed**; ratified only if the EXTRA Phase 5 is green-lit after the Phase 4 checkpoint. No broker code before then.

## Test coverage

Placeholder only — the test pipeline itself is verified (`dotnet test` runs, 1/1 passes). Real behavioral tests begin in Phase 2 (TDD).

## Deviations from plan

- **.NET SDK was not installed** on the dev machine → installed 8.0.422 via winget; `C:\Program Files\dotnet` is not on PATH in fresh shells (workaround documented in CLAUDE.md §7).
- **NuGet had zero configured sources** → initial xUnit restore failed (NU1100); fixed by adding nuget.org.
- **GitHub repo already existed** (`nuri35/dynamic-config-net`) → repo creation skipped, pushed to existing remote.
- **Phase 0 landed in three commits** instead of one: the scaffold was committed first, the docs/ADR/skills workflow second, and a third restructured everything to the CORE-first/EXTRA-later delivery strategy (RabbitMQ moved out of docker-compose and out of "current" docs into planned-EXTRA status; ADRs renumbered so core decisions are 0001–0003).
