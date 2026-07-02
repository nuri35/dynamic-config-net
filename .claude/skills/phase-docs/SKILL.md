---
name: phase-docs
description: Use when a development phase completes, or the user says "phase bitti" / "close the phase" / asks for phase documentation. Writes docs/phases/phase-N.md from the template, updates the CLAUDE.md phase and locked-decisions tables, and drafts ADRs for any architectural decision made during the phase.
---

# Phase Docs

Close out a completed phase by making the documentation match reality. All three artifacts land in the **same phase commit** as the code.

## Checklist

1. **Write `docs/phases/phase-N.md`** using the template below. Write it now, from what actually happened — never retroactively at project end.
2. **Update `CLAUDE.md` → Phase Table**: status → `done`, completion date (absolute, YYYY-MM-DD), one-line outcome, link to the phase doc.
3. **Harvest decisions**: scan the phase's conversation/diff for architectural decisions made or revised. For each one:
   - Write or update the ADR in `docs/adr/` (next sequential number, format of ADR 0001: Status / Date / Context / Decision / Consequences / Alternatives Considered).
   - Add or update the row in `CLAUDE.md` → Locked Decisions with the ADR link.
4. **Check `docs/architecture.md`** — if the phase changed a component boundary, a data flow, or a failure mode, update the diagram/tables.
5. **Check `README.md`** — if the phase completed a feature the README presents as finished, make sure reality now matches the claim (ports, commands, coverage tables).

## Phase doc template

```markdown
# Phase N — <Title>

**Status:** done · **Completed:** YYYY-MM-DD · **Commit:** <hash>

## Goal
<What this phase set out to deliver, 2-3 sentences.>

## What was built
<Bullet list of concrete artifacts: classes, endpoints, tests, infra.>

## Key decisions
<Each decision made during the phase, one line each, linking its ADR: [ADR 000X](../adr/000X-*.md). "None" if none.>

## Test coverage
<What behavior is now proven by tests, and how it's run. "N/A" only for pure-docs phases.>

## Deviations from plan
<Anything that differed from the phase plan and why. "None" if none.>
```
