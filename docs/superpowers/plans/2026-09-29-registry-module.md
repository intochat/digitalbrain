# Registry Module Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan in the current session. Track steps below.

**Goal:** Consolidate discovery and neuron registration in Registry and remove their ownership from Kernel.

**Architecture:** Kernel publishes an immutable selected-module inventory. Registry owns discovery startup, invocation, search, and neuron tool factories. AI depends only on its generic tool contracts.

**Tech Stack:** Existing C#/.NET, Orleans, Microsoft.Extensions.AI, and xUnit projects.

**Spec:** `docs/superpowers/specs/2026-09-29-registry-module-design.md`

## Global Constraints

- Preserve Orleans aliases, grain types, storage names, method IDs, and `/discovery/unmet-intents`.
- Keep Kernel and the shared unit harness independent of Registry.
- Discover only selected modules; preserve existing search semantics.
- Work in the existing clean `refactor/csharp-module` checkout; leave the changes reviewable without committing.

## Review Focus

- Registry omitted: ordinary runtime hosts still work without discovery services.
- Module order: Registry sees selected modules even when configured before them.
- Loaded but unselected contracts never enter registry results.
- Neuron methods offered by capability search have matching callable tools.
- Production and test hosting publish the same module inventory semantics.

## Task 1: Consolidate ownership and composition

- [x] Add and run regression tests for absent registry services and Registry-owned neuron tools; observe expected failures.
- [x] Rename Discovery paths, projects, namespaces, and module types; update solution, consumers, deployment fixtures, and Docker references.
- [x] Move neuron registry, startup, metadata, invoker, and neuron tool adapter into Registry.
- [x] Add `ModuleInventory(IEnumerable<Type>)` with immutable `Types`; register through `AddDigitalBrain(IEnumerable<Type>)` in production and tests. Register registry services and startup inside `RegistryModule.Configure`.
- [x] Move registry/invoker/Time and neuron schema tests to Registry, preserving generic AI tests in AI.
- [x] Run Registry and Kernel tests and resolve migration failures.

## Task 2: Verify consumers and documentation

- [x] Update module README and superseded design notes.
- [x] Run affected AI, Assistant, composition/deployment tests and solution build.
- [x] Review dependency direction, residual old names, aliases, and the final diff; address concrete findings.
- [x] Record verification results and report any environmental limitations.

## Verification results

- `dotnet build DigitalBrain.slnx -p:CodeGraphRefresh=false`: passed, zero warnings and errors.
- Registry: 27 passed; Kernel runtime: 76 passed; AI unit: 78 passed; Assistant: 24 passed; Time: 20 passed; IntoChat deployment: 2 passed (227 total).
- IntoChat unit: 63 passed, 3 failed because files already absent from HEAD are required by `HostedDeploymentFacts.TelemetryCollectorTailSamplesToAPersistentBackend` (`ops/otel/collector.yaml`), `HostedDeploymentFacts.RunbookCoversIncidentBackupUpgradeAndSlo` (`docs/operations/runbook.md`), and `PathTruthFacts.DecisionAndEpicRegistersExist` (`docs/product/decisions/README.md`).
- The broader solution run also passed Apps unit (41), Kernel E2E (6), AI E2E (1), Gmail E2E (3), CSharp unit (40), ClickHouse unit (4), and DotNet unit (2). Supabase unit had 51 passes and one failure: `LiveTableArchitectureFacts.ExactlyOneLiveTableClusterRemainsAndTheItableAdapterIsAdrFenced` requires the already-missing `docs/product/decisions/0002-one-live-table-contract.md`.
- Full solution test run was interrupted during IntoChat E2E while Flutter web compilation remained unhealthy; full E2E coverage is unverified.
- Changed C# files passed `dotnet format whitespace --verify-no-changes`; `git diff --check` passed.
- Independent read-only review found no actionable regressions. Existing invoker/schema semantics and the Qdrant dependency remain unchanged; preserved aliases and URL are intentional compatibility constraints.
- Updated `.gitignore` so the moved `Registry/.../Sources/NeuronCapabilitySource.cs` is included in the change.
