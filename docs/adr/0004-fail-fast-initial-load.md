# ADR 0004: Fail-Fast on Initial Load

- **Status:** Accepted
- **Date:** 2026-07-02 (decided in Phase 3)

## Context

The reader's constructor performs the first snapshot load. If storage is unreachable at that moment, there are two defensible behaviors: **fail-fast** (let the storage exception propagate out of the constructor) or **fail-soft** (start with an empty snapshot and keep retrying in the background). The case's resilience clause — "the application continues with the last **successful** configuration records" — governs runtime outages; it is silent about boot-time outages, so the decision is ours to make and record.

## Decision

**Fail-fast.** A storage failure during the constructor's initial load propagates to the caller, unwrapped. The background polling loop is only started after a successful first load.

Reasoning:

1. **The case's own wording presupposes a successful load.** "Last successful records" implies at least one success has happened; before that point the fallback mechanism has nothing to fall back *to*. Fail-fast keeps the invariant "a constructed reader always has a real snapshot" — every downstream code path (refresh, fallback, GetValue) can rely on it.
2. **A config-less service is not a working service.** With an empty snapshot, every `GetValue` throws `ConfigurationKeyNotFoundException` until storage recovers — the service is up but misbehaving on each request, which is strictly worse to operate than a service that visibly failed to start.
3. **Fail-fast composes with the platform.** Orchestrators (Kubernetes, systemd, IIS rapid-fail) already implement restart-with-backoff for crashing processes; a boot-time throw plugs into that machinery for free. Fail-soft would re-implement retry inside the library and *hide* the outage from the platform's health signals.

## Consequences

**Positive**
- The invariant "constructed ⇒ loaded" makes the runtime fallback trivially correct: the snapshot reference is never null, never empty-by-accident.
- Boot-time storage outages are loud and observable at the right layer (process start), not smuggled into per-request errors.
- Pinned by a unit test (`Constructor_InitialLoadFails_PropagatesTheStorageException`), so any future behavior change is a visible, deliberate test change.

**Negative**
- A service cannot boot through a storage outage even if it could theoretically do useful config-free work — accepted: for this system's consumers, configuration is a hard dependency.
- Callers constructing the reader lazily (first request instead of startup) feel the throw at an awkward time — mitigated by documenting construct-at-startup as the intended usage.

## Alternatives Considered

- **Fail-soft: empty snapshot + background retry** — keeps the process alive, but every `GetValue` throws until recovery (a degraded zombie rather than a degraded-but-alive service), weakens the "constructed ⇒ loaded" invariant everything else leans on, duplicates the orchestrator's restart job inside the library, and turns a clear boot failure into a stream of confusing runtime errors. Rejected.
- **Configurable behavior (flag or timeout)** — the case freezes the constructor at three parameters; there is no public seam for the option, and inventing one violates the frozen surface. Rejected on contract grounds.
