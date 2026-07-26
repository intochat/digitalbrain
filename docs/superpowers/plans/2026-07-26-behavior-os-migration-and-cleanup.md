# Behavior OS Migration and Cleanup Implementation Plan

> **Status:** Designed/current. This stable index retains constraints, order, and task navigation; it does not claim a Behavior execution, installation, or parity-retirement rail is built.

**Goal:** Move every surviving UI and account-enrichment policy into installed OS Behaviors, delete the compiled composition/process paths, and make code, tests, packages, and documentation describe one built system.

**Architecture:** Source-controlled OS artifacts compose Flutter, Time, AI, Google, and Salesforce module vocabulary through the same Behavior rail proven in prior plans. Product tests move from “Compositions” to “OperatingSystem,” assert journals and edge outcomes, and become deletion gates; only then are the two sample projects, their process contracts/facts, duplicate helpers, and stale claims removed.

**Tech Stack:** Signed built-in Behavior artifacts, existing Flutter/Time/AI/Google/Salesforce module contracts and runtimes, Reqnroll/xUnit v3, DigitalBrain test brain, root package/boundary tests, VitePress docs.

## Global Constraints

- Modules retain vocabulary/provider mechanics; no Behavior logic remains in a module implementation.
- OS policy is one-file Behavior source plus manifest/schema/features; files are embedded as artifacts, not compiled as trusted grain classes.
- Start/UI/account Behaviors use exact module contracts and grants; they add no public CLR neuron or synapse vocabulary.
- Human Salesforce approval remains durable evidence from the owner session and is validated by the Salesforce module.
- Account-enrichment private request history moves to `BehaviorNeuron` state; it is not duplicated in another process neuron or database.
- The product has one activation-to-first-screen path and one account-enrichment path.
- Every current composition either becomes a named OS Behavior with product value or is deleted; no wrapper/helper layer survives.
- Rename `DigitalBrain.Compositions.Tests` to `DigitalBrain.OperatingSystem.Tests`; delete source-grep shape tests and keep journal/edge product proofs.
- Remove projects and references in the same commits as their green replacements.
- Historical design/grill documents remain only as clearly archived evidence; current docs contain no “rail unbuilt,” pre-rail, or compiled-Behavior claims.
- No empty directories, build output, temp artifacts, commented-out code, redundant samples, obsolete tests, or contradictory package descriptions remain.

---

## Task Allocation and Required Order

1. [Task 1 — Move the product test home and freeze replacement outcomes](2026-07-26-behavior-os-migration-and-cleanup-replacement.md#task-1-move-the-product-test-home-and-freeze-replacement-outcomes)
2. [Task 2 — Re-express shell and surface policy as OS Behavior artifacts](2026-07-26-behavior-os-migration-and-cleanup-replacement.md#task-2-re-express-shell-and-surface-policy-as-os-behavior-artifacts)
3. [Task 3 — Re-express account enrichment as one multi-entry Behavior](2026-07-26-behavior-os-migration-and-cleanup-replacement.md#task-3-re-express-account-enrichment-as-one-multi-entry-behavior)
4. [Task 4 — Register the built-in OS through hosting and test fixtures](2026-07-26-behavior-os-migration-and-cleanup-cutover.md#task-4-register-the-built-in-os-through-hosting-and-test-fixtures)
5. [Task 5 — Delete the compiled composition and account-process projects](2026-07-26-behavior-os-migration-and-cleanup-cutover.md#task-5-delete-the-compiled-composition-and-account-process-projects)
6. [Task 6 — Rewrite current documentation and archive superseded decisions](2026-07-26-behavior-os-migration-and-cleanup-documentation-and-cleanup.md#task-6-rewrite-current-documentation-and-archive-superseded-decisions)
7. [Task 7 — Remove repository trash and prove one coherent system](2026-07-26-behavior-os-migration-and-cleanup-documentation-and-cleanup.md#task-7-remove-repository-trash-and-prove-one-coherent-system)

Tasks execute strictly in numeric order. Tasks 1–3 are replacement work; Task 5 is forbidden until the green replacement proofs from Tasks 1–4, including the explicit parity and deletion gates, pass. A new Behavior type, compile success, or shape-only test is never parity evidence; no old and new route may execute for one command. This plan retains the Behavior rail as Designed: no public/version decision, implementation claim, or parity-route retirement is authorized by this extraction.

## Extraction Integrity and Link Map

- Red baseline: 516 physical lines; SHA-256 `62BD145614C121030171EDCAF93ACF9D20A010FAF2D6B485D8492C4FDA96B0E6`.
- Allocation: replacement owns Tasks 1–3; cutover owns Tasks 4–5; documentation/cleanup owns Tasks 6–7. Each task occurs exactly once.
- Normalized task/file/command/parity/delete-order corpus SHA-256: `0d00ea0a666010e831ef19a2bf97d5a1cf464e2991a323cd83a475f1725ae310` (UTF-8 task corpus, LF-normalized, original lines 27–516). It contains 7 task headings, 7 Files blocks, 27 Run/code-fence command markers, 8 parity markers, and 28 delete markers.
- Reconstruction: concatenate this index through the final global constraint, then the task records 1–3, 4–5, and 6–7 in order. Task text, files, commands, rationales, and acceptance expectations are retained unchanged; removed text is only the worker-session preamble.
- Stable links: this root URL remains the inbound authority target; it links to every task record, and every task record links back here. The heading map is Task 1–3 → `replacement`, Task 4–5 → `cutover`, and Task 6–7 → `documentation-and-cleanup`.
- Authorities: [Behavior authority](../../architecture/behaviors-registry-and-discovery.md) and [hosting authority](../../architecture/hosting-durability-testing.md) remain current; this plan remains Designed/current.
- Scope: all implementation commands, paths, interfaces, and task acceptance expectations remain plans, not evidence that the rail is built.
