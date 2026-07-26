# Behavior Foundation and Admission Implementation Plan

> **Status:** Designed/current. This stable index retains plan scope, constraints, order, and task navigation; it does not claim the admission rail is built.

**Goal:** Add the stable Behavior authoring contracts and a compile-once admission pipeline that produces immutable, content-addressed evidence without loading candidate code into the silo.

**Architecture:** `DigitalBrain.Behaviors` is the small packable SDK; `DigitalBrain.Behaviors.Runtime` owns trusted admission/storage adapters; `DigitalBrain.BehaviorBuilder` runs restore, build, analyzers, and PE checks in a child boundary. Artifact bytes are canonical, SHA-256-addressed, create-only in a separate blob container, and remain ineligible for approval until the sandbox plan supplies green BDD evidence.

**Tech Stack:** Microsoft .NET SDK 10.0.302 file-based apps and CLI, Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0, System.Reflection.Metadata/PEReader, Reqnroll.xUnit.v3 3.3.4, Azure.Storage.Blobs 12.29.1, Aspire.Azure.Storage.Blobs 13.4.6, System.Security.Cryptography.Cose 10.0.10, JsonSchema.Net 9.3.0 behind a license gate.

## Global Constraints

- Candidate source is one `.cs` file and contains no user-controlled `#:` directives.
- Repository and Behavior compilation use Microsoft .NET SDK `10.0.302`, `rollForward: disable`, and `allowPrerelease: false`; install that supported SDK before execution because the current machine exposes only a 10.0.400 preview build.
- The exact installed .NET SDK executable is the only compiler; do not embed Roslyn or call `dotnet run`/`dotnet pack`.
- Restore uses a fresh package cache, a read-only vetted local feed, `packages.lock.json`, and a second clean `--locked-mode` pass with no network.
- Contract packages may contain managed contract assemblies only; reject build targets, analyzers, tools, content, native/runtime assets, and provider implementations.
- Compiler warnings and policy diagnostics are errors.
- `PEReader` and `MetadataReader` run only inside the constrained builder process; the silo never parses candidate PE/PDB bytes.
- A proposal may supply `.feature` text but never C# bindings, hooks, plugins, or test configuration.
- The unsigned deterministic artifact envelope is the revision identity; a detached COSE signature is provenance only.
- The journal is authority; Blob Storage is an untrusted immutable byte store in a container separate from the journal.
- Intent schemas require draft 2020-12, are self-contained, reject unknown keywords/remote references, and are hidden behind `IIntentSchemaValidator`.
- `JsonSchema.Net` 9.3.0 may be restored only after `eng/approved-dependencies.json` records explicit acceptance of its binary EULA.

---


## Task Allocation and Required Order

1. [Task 1 — Add the SDK, runtime, builder, and focused test boundaries](2026-07-26-behavior-foundation-and-admission-sdk-and-identities.md#task-1-add-the-sdk-runtime-builder-and-focused-test-boundaries)
2. [Task 2 — Add stable Behavior identities and the safe program SDK](2026-07-26-behavior-foundation-and-admission-sdk-and-identities.md#task-2-add-stable-behavior-identities-and-the-safe-program-sdk)
3. [Task 3 — Define canonical manifests, grants, schemas, and artifact envelopes](2026-07-26-behavior-foundation-and-admission-sdk-and-identities.md#task-3-define-canonical-manifests-grants-schemas-and-artifact-envelopes)
4. [Task 4 — Generate stable synapse and module capability catalogs](2026-07-26-behavior-foundation-and-admission-build-and-admission.md#task-4-generate-stable-synapse-and-module-capability-catalogs)
5. [Task 5 — Build with the exact SDK and a vetted offline contract feed](2026-07-26-behavior-foundation-and-admission-build-and-admission.md#task-5-build-with-the-exact-sdk-and-a-vetted-offline-contract-feed)
6. [Task 6 — Enforce source policy and sandboxed PE admission](2026-07-26-behavior-foundation-and-admission-build-and-admission.md#task-6-enforce-source-policy-and-sandboxed-pe-admission)
7. [Task 7 — Validate intent schemas, provenance, and immutable blob storage](2026-07-26-behavior-foundation-and-admission-evidence.md#task-7-validate-intent-schemas-provenance-and-immutable-blob-storage)
8. [Task 8 — Build the trusted Reqnroll verification vocabulary](2026-07-26-behavior-foundation-and-admission-evidence.md#task-8-build-the-trusted-reqnroll-verification-vocabulary)
9. [Task 9 — Prove reproducibility and document the admitted-not-approved boundary](2026-07-26-behavior-foundation-and-admission-evidence.md#task-9-prove-reproducibility-and-document-the-admitted-not-approved-boundary)

Tasks execute strictly in numeric order. Each task retains its exact files, interfaces, commands, rationale, acceptance proof, and commit boundary in its responsibility record. Do not begin a later task until the preceding task has its named proof.

## Extraction Integrity and Link Map

- Red baseline: 746 physical lines; SHA-256 `EB9D0E3BD24317649CD63629687E2934E5BF23C836217254141FA13D4D7965AE`.
- Allocation: SDK/identities owns Tasks 1–3; build/admission owns Tasks 4–6; evidence owns Tasks 7–9. Each task occurs exactly once.
- Reconstruction: concatenate this index through the final global constraint, then Tasks 1–3, 4–6, and 7–9 in order; task text is retained unchanged. The removed worker/checklist preamble is session ceremony, not task semantics.
- Stable links: the root URL remains the inbound authority target; this index links to every task record, and every task record links back here.
- Scope: this is a Designed/current implementation plan. All implementation commands, paths, interfaces, and task acceptance expectations remain plans, not evidence that the rail is built.
