# Stage 1 owner-amendment resolution — 2026-08-11

> **STATUS: RESOLVED by Vlad.** This amendment supersedes the conflicting historical brief and
> test directives. Implementation continues under the amended source-first gate.

## Owner direction

1. Keep `DigitalBrain.Modules.Salesforce.Contracts`; module neuron interfaces and synapses are
   product contracts, not janitor trash.
2. The central automated-test project is intentionally deleted. Do not create or run automated
   tests during the refit; production source is the current truth.
3. Design the proper testing framework in final hardening, with tests owned by each module rather
   than one central `DigitalBrain.Tests` project.

## Conflicting binding text

- `plans/stage1/briefs/J-batch.md` item J2 and
  `plans/RATIFIED-PRODUCT-DEFINITION.md` "Known trash" require deletion of the Salesforce
  contracts project.
- `GROK.md` requires both accepted test kinds to live in `src/Tests/DigitalBrain.Tests`.
- `plans/GROK-ORCHESTRATION-STAGE1.md` describes one merged test suite and excludes project
  consolidation from Stage 1; its remaining order is janitor → Flutter → docs → exit.

## Reconciliation performed

- Stopped the active targeted test run before changing project structure.
- Restored the Salesforce contracts project, its project reference, and its solution entry.
- Vlad committed the reconciled source plus intentional central-suite deletion in
  `4a52255361efded2c73e2e49d100baafeaea239c`.
- Amended `GROK.md`, the ratified definition, Stage-1 plan, handoff, CI, and gate together so they
  all use source characterization, adversarial review, build/static analysis, and live smoke.

## Binding resolution

No test-project migration occurs in Stage 1 or as the opening Stage-2 seam. Testing architecture is
deferred until final hardening, after product seams and module ownership stabilize. Existing Flutter
test files are left untouched but are not current authority and are not executed.

## Verification

- Confirmed no grok, DigitalBrain, AppHost, or Aspire process was running before the build.
- `pwsh -NoProfile -File scripts/gate.ps1` — PASS, 0 warnings, 0 errors.
- The gate executed the full solution build only; no automated test command ran.
- The build compiled `DigitalBrain.Modules.Salesforce.Contracts`, proving the retained project is
  still part of the solution graph.
