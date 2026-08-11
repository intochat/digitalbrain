# Stage 1 owner-amendment conflict — 2026-08-11

## Owner direction

1. Keep `DigitalBrain.Modules.Salesforce.Contracts`; module neuron interfaces and synapses are
   product contracts, not janitor trash.
2. Tests belong with their modules rather than in one central `DigitalBrain.Tests` project.

## Conflicting binding text

- `plans/stage1/briefs/J-batch.md` item J2 and
  `plans/RATIFIED-PRODUCT-DEFINITION.md` "Known trash" require deletion of the Salesforce
  contracts project.
- `GROK.md` requires both accepted test kinds to live in `src/Tests/DigitalBrain.Tests`.
- `plans/GROK-ORCHESTRATION-STAGE1.md` describes one merged test suite and excludes project
  consolidation from Stage 1; its remaining order is janitor → Flutter → docs → exit.

## Safe reconciliation performed

- Stopped the active targeted test run before changing project structure.
- Restored the Salesforce contracts project, its project reference, and its solution entry.
- Made no commit and did not continue the gate under contradictory rules.

## Decision required before implementation resumes

The binding definition, standing orders, and stage plan must be amended together. In particular,
the modular test-project migration must be placed either before the remaining Stage-1 Flutter lane
or as the first Stage-2 structural seam. Silent placement would violate the ordered handoff.
