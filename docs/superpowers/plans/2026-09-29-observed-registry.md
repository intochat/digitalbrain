# Observed Registry Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation, with test-first checks and a final independent review.

**Goal:** Replace the capability machinery with a small signal-maintained neuron registry.
**Architecture:** Kernel publishes host-local runtime signals; Registry reads module snapshots, searches type vectors, and persists observed instances in a neuron.
**Tech Stack:** Existing C#, Orleans, channels, Microsoft.Extensions.AI, Qdrant, xUnit.
**Spec:** `docs/superpowers/specs/2026-09-29-observed-registry-design.md`

## Global Constraints

- Kernel must not depend on Registry.
- Neuron activation must not await Registry or vector services.
- Instance activity is last-known observation, not authoritative liveness.
- Remove the old capability/invocation machinery rather than relocating it.
- Work in the current checkout. Commit only when requested.

## Review Focus

- Late module subscribers and startup ordering.
- Failed activation, overridden activation hooks, and self-observation loops.
- Registry reactivation must retain observed history.
- Duplicate and stale activity must not deactivate a newer activation.
- Vector results must belong to the current type snapshot, and deleted tools must no longer be advertised.

## Tasks

- [x] Test and implement Kernel runtime signals, module replay, startup announcements, and automatic lifecycle notifications.
- [x] Replace Registry contracts/implementation/tests with type metadata, vector search, and persistent instance observations.
- [x] Remove deleted capability and tool dependencies from Apps, AI policy, and Assistant; update tests and docs.
- [x] Build, run affected suites, check formatting, and request independent review.
- [x] Record verification and limitations.

## Verification

- Solution build: passed, zero warnings and errors.
- 253 tests passed: Kernel runtime 80, Registry 7, AI 77, Apps 41, Assistant 24, Time 20, Kernel deployment 2, IntoChat deployment 2.
- Regression reproduced and fixed: replaying an activation after its same-timestamp deactivation cannot resurrect that activation.
- Changed C# whitespace verification and git diff checks passed.
- Independent read-only review found no actionable correctness issues. Additional coverage of persistence-write failure/retry and vector reindexing after a changed module snapshot remains optional.
- Full browser E2E suite was not rerun. Existing unrelated missing-document failures in IntoChat/Supabase were not changed.
- History remains best effort and last-known-active; Registry excludes itself. Vector search needs Qdrant and an embedding provider.

## Follow-up: shared signal transport

Removed the separate RuntimeSignal hierarchy and RuntimeSignals service. LocalSignalHub now supports typed, bounded silo-wide subscriptions alongside its existing neuron subscriptions. Lifecycle records derive from Signal; Registry subscribes to NeuronActivity. Type discovery reads the existing immutable ModuleInventory, so module notifications need no replay or second snapshot store.

Verification: solution build passed with zero warnings/errors; Kernel runtime 80, Registry 7, and Time 20 tests passed (107 total). Changed-file whitespace verification passed. Independent review found no actionable issues.