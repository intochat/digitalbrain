# DigitalBrain Architecture Refactoring Plan — Self-Evolving OS Focus

**Date:** 2026-07-06  
**Based on:** `docs/architecture-trash-analysis-2026-07-06.md` (30% trash claim at time of writing; significant dead code + wrapper projects already removed per later cleanups).  
**Status:** Planning only. Execution follows AGENTS.md (delete > add, fast inner loop, small slices).  
**Product North Star:** Make safe, explicit, journaled, human-approved self-evolution the *only* path for user-visible mutations. Ino + packs + foundry + marketplace all feed the same rail. The OS evolves itself durably and reversibly.

## 1. Current State (verified 2026-07-06)

- **Build:** `dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true` → clean (0 errors, 2 minor nullability warnings in PrototypeJournals).
- **Self-evolution rail:** Live and tested.
  - Contracts + `ISelfEvolutionNeuron`, `SelfEvolutionProposal`/`Decision`/`Risk` etc. in `DigitalBrain.Core/SelfEvolution.cs` (additive only).
  - `SelfEvolutionNeuron` (grain) + `SelfEvolutionApplyRegistry` + `ISelfEvolutionApplyHandler` in `DigitalBrain.Kernel/SelfEvolution/`.
  - Domain handlers: `MarketplaceInstallApplyHandler`, `AutomationDefinitionApplyHandler`, `FoundryRunApplyHandler`, `FoundryDeployApplyHandler`.
  - Staging sites: `MarketplaceNeuron`, `CodeFoundryClosedLoopNeuron`, `InoNeuron` (multiple), `SoftwareEngineeringClosedLoopNeuron`, MCP `define_reaction` / `run_code_foundry`.
  - Projection + apply + rollback-required + durability replay tests exist and green (15+ targeted self-evo tests pass). Broader non-cluster filter (SelfEvolution + MarketplaceInstallApproval + CodeFoundryApproval + related): 276 passed, 0 failed, 8 skipped (E2E).
- **Bypasses (intentional for dev speed):** `TrustedLocalInstallBypass` (Marketplace), `TrustedAutoApply` (Foundry). Explicit config, test-only by default. Non-bypass path goes through proposal → decision → handler.
- **Trash status:** Empty `DigitalBrain.Developer*` / `DigitalBrain.Windows*` projects and references removed. Ino language editor (old .ino) largely pruned in prior passes. PrototypeJournals + SystemRollingSurfaces remain for evaluation.
- **Large files / split candidates (still valid):**
  - `DigitalBrain.Kernel/Ino/InoNeuron.cs` (~800+ LOC): intent classifier + Gmail/Salesforce/market/schema + LLM + memory + self-evo staging mixed.
  - `DigitalBrain.Kernel/Gateway/GatewayService.cs`: large request switch.
  - `Marketplace.Contracts/MarketplaceUiSurfaces.cs`.
- **Aspire health:** `aspire doctor` → 5/5 pass (CLI 13.4.6, AppHost 13.4.6, SDK, certs, Docker).

The rail is no longer "dangling" (slice 1 complete). Gaps now are: full side-door closure (config-gated), durability for the self-evo stream specifically, rollback procedure completeness, and moving logic out of monoliths so the rail + Ino evolution paths are reviewable and extensible.

## 2. Requirements Less Dumb (AGENTS Step 1)

Questioned:
- "30% trash" — snapshot at analysis time. Re-measure via file counts + dead-path searches before big deletes. Current tree is already leaner.
- "No bypasses ever" — hurts inner dev loop and seed installs. Keep explicit, named, default-off `Trusted*` flags + tests that prove they are off for user paths.
- "Everything must be a neuron" — good model, but contracts + handlers live in appropriate assemblies (Core for wire types, Kernel for runtime grains/activation).
- "Prototype journals are always bad" — name is honest until we land durable self-evo-only store. Do not rename prematurely.

Result: Focus slices on (a) rail completeness + durability, (b) logic splits that make evolution paths obvious, (c) delete only proven dead after searches.

## 3. Delete First (AGENTS Step 2)

Candidates (verify live paths first):
- `SystemRollingSurfaces.cs` + referencing test (if no production emit/consume path).
- Any remaining dead Ino editor / CompilerNeuron / Software20TeamNeuron references (post-prior clean).
- Over-duplicated test data or spike tests that never run in fast lane.
- Stale docs/specs/plans under `docs/superpowers/` that are not marked active (follow existing lifecycle rule).

Do **not** delete:
- Living Ino assistant paths (`InoRequest`/`IInoNeuron`/`InoNeuron.cs` "ino-main").
- Trusted bypass configs (they protect cycle time).
- PrototypeJournals until self-evo durability lands.

## 4. Simplify (AGENTS Step 3)

Target shape for self-evolution:
```
Core/
  SelfEvolution.cs          # wire types + ISelfEvolutionNeuron + ApplyVia constants + risk enum (stable, additive)
Kernel/
  SelfEvolution/
    SelfEvolutionNeuron.cs  # canonical proposal/decision consumer + projection + dispatch to registry
    SelfEvolutionApplyHandler.cs  # registry (risk + duplicate guard)
  Foundry/
    ...FoundryApplyHandlers.cs
  MarketplaceInstallApplyHandler.cs
  AutomationDefinitionApplyHandler.cs
  Ino/...
  Gateway/...
```
- One place decides "approved? → apply". Handlers own the *effect* (install pack, register script, run/deploy code).
- Ino becomes thin orchestrator + pluggable intent handlers; new "create neuron/automation/pack" requests go through the rail.
- Gateway handlers per domain (already partially extracted).

## 5. Accelerate Cycle Time (AGENTS Step 4) + Fast Inner Loop

Default verification (no cluster/E2E unless touching rail + durability):
```pwsh
dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~SelfEvolution|FullyQualifiedName~MarketplaceInstallApproval|FullyQualifiedName~CodeFoundryApproval|Category!=cluster"
aspire doctor
```

- Keep/add narrow `SelfEvolution*` tests.
- Use `WaitUntilAsync` helper for any remaining poll loops.
- Before side-door or journal changes: targeted Aspire resource commands (via MCP) + logs, not full `aspire run`.
- Architecture guard tests (CoreBoundaryTests + future module direction tests) must stay green.

## 6. Automate Last (AGENTS Step 5)

- Self-evo rail tests + durability replay already automated.
- Future: MCP tool or test harness that stages proposal → approves via decision → asserts apply + journal + optional rollback.
- Aspire MCP (`aspire__execute_resource_command`, logs) for kernel restart scenarios in rail tests.
- When adding new mutation surface (new pack type, new automation), require a rail test.

## 7. Prioritized Refactoring Slices (Small, Verifiable, Self-Evo First)

| # | Slice | Focus | Why (Self-Evo) | Size | Verification |
|---|-------|-------|----------------|------|--------------|
| 1 | Close remaining side doors | Flip defaults + remove unconditional bypasses (keep config flags + explicit trusted seeds) | Every user mutation must stage proposal. | S | Marketplace/Foundry approval tests; `define_reaction` test; no direct `ApplyImmediately` in non-trusted path. |
| 2 | Durable self-evolution journal | Narrow durable store / journal config only for the self-evo grain (or rely on Orleans journal + prove replay). Rename `PrototypeJournals` only for this path later. | Audit trail survives restart (core promise). | M | `SelfEvolutionDurabilityTests` + reactivation that replays proposals/decisions/applies. |
| 3 | Full rollback procedure | Wire `SelfEvolutionRollbackRequired` → `CheckpointRestoreTrigger` / restore executor. Record checkpoint in every apply result. | propose → approve → apply → rollback is complete and testable. | M | Rollback tests; foundry failure path emits usable checkpoint. |
| 4 | Split InoNeuron | Extract per-intent handlers (Gmail, Salesforce, marketplace, schema, LLM, self-evo creation) behind classifier. Keep Ino as orchestrator + rail caller. | "Ino creates neurons/automations" becomes reviewable; new evolution surfaces don't bloat one file. | M | Ino tests + new routing tests per handler. Ino still delivers proposals for creation paths. |
| 5 | Gateway handler extraction | Continue domain handlers for Send (marketplace, auth, config, ino, telegram). | Clean routing = easier to see evolution-related commands. | S | Gateway tests. |
| 6 | Marketplace UI surfaces split | Extract action generator from `MarketplaceUiSurfaces.cs`. | Surface for approvals/approvals-list stays clean. | S | Marketplace UI + facet tests. |
| 7 | Thin-wrapper merges (if still present) | `Telegram.Channel` → Telegram, `UiKit` → Ui.Contracts (update Core comment + guards). | Less projects, same boundaries. | S | Build + CoreBoundaryTests + moved interface tests. |
| 8 | Core minimalism audit | Scan for non-primitive pollution post-splits. Move only if it does not increase blast radius for packs. | Core remains the safe contract surface packs + rail depend on. | S | Architecture tests; pack embodiment tests. |
| 9 | Test lanes & de-flake | Unit vs runtime vs pack vs e2e filters; replace fixed delays with WaitUntil. | Faster feedback on rail changes. | S/M | Affected tests repeat green; fast lane time down. |
| 10 | Self-evo surfaces + MCP polish | Dedicated surfaces for pending proposals / decisions (via existing Ui). Enhance MCP `ino_list_proposals` / approve with better ids. | Human (and Ino) can inspect/approve evolution without custom tools. | M | UI contract + MCP tests. |

**Commit discipline:** One slice (or sub-slice) per commit. Each: build + targeted tests + `aspire doctor`. Large-file splits after rail is solid.

## 8. Logic Placement Rules (Post-Refactor)

- **Wire / stable contracts:** `DigitalBrain.Core` (SelfEvolution* records, INeuron, Synapse base, ids, minimal system messages). Never domain feature data.
- **Self-evolution rail core:** `Kernel/SelfEvolution/` (grain + registry). Domain-agnostic.
- **Apply effects:** Colocated with the domain that owns the mutation (`Foundry/`, `Marketplace*Handler.cs`, `Automation*`).
- **Ino evolution requests:** Ino orchestrator stages `SelfEvolutionProposal`; actual creation/activation happens only in approved apply handler.
- **New neurons from packs:** Always via `NeuroPackInstalled` + `GeneratedNeuron` (already wired; ensure proposal for user installs).
- **No stringly apply outside registry:** All `ApplyVia` values from `SelfEvolutionApplyVia` consts.
- **Trusted seeds:** Documented in `Program.cs` / seeds + marked in tests. Never the default for user/MCP paths.

## 9. Risks & Mitigations

- Breaking user automations or pack installs: Mitigate by keeping bypass flags during transition + extensive approval-path tests.
- Journal replay changes: Self-evo synapses are additive; never mutate existing record shapes.
- Large refactors regressing broadcast/embodiment: Run smoke filters (`Broadcast|PackAlcEmbodier|Generated|Ino|SelfEvolution`) after every batch.
- Cycle time regression: Default to non-cluster tests; use Aspire MCP resource commands for restart scenarios.

## 10. Done Criteria (Self-Evolving OS Ready)

- All non-seed user mutation paths (marketplace user install, `define_reaction`, foundry run/deploy, Ino "create automation/neuron") stage a proposal and only execute after `SelfEvolutionDecision.Approved`.
- Proposals/decisions/apply-results/rollback-required visible in durable journals and replay correctly.
- Rollback is executable (checkpoint id flows through).
- Ino + Gateway + large surfaces are split enough that adding a new evolution capability touches small reviewable files.
- Fast tests + `aspire doctor` green; no new trash introduced.
- Architecture guard tests prevent Core bloat and direct bypasses (except explicit trusted config).

## 11. Next Actions (Do Not Start Coding)

1. Approve this plan (or specific slices).
2. Run full relevant non-E2E suite once before first slice: `dotnet test ... --filter "..."`.
3. Execute slice-by-slice with the verification matrix above.
4. When touching Aspire wiring or host (kernel restart apply), use `aspire__*` MCP tools + `aspire doctor`.
5. Use Context7 for any new Orleans/Aspire/ .NET API surface before implementation (per project rules).

**This plan makes the self-evolving capabilities the explicit, testable, central feature instead of an aspirational rail with side doors.**

---
*Generated from analysis + current code inspection. Follow AGENTS.md for execution.*
