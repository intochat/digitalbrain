# DigitalBrain / NeuroOS — Architecture & Trash Analysis (2026-07-06)

Continuation of `ARCHITECTURE_CLEANUP_PROPOSAL.md`. Product lens first: DigitalBrain is a
self-evolving AI-native OS (neurons/synapses/packs/kernel), and Ino is the personal assistant
that can create neurons/synapses on user request and run user-created automations. Everything
below is judged against one product question: **does this help or hurt a safe, explicit,
testable self-evolution path?**

## 0. Critical finding: master does not compile (proven)

`DigitalBrain.Kernel/SoftwareEngineeringClosedLoopNeuron.cs` (committed, lines 87–96) fires
`new SelfEvolutionProposal(...)` with `SelfEvolutionRisk.KernelRestart`, but **no definition of
`SelfEvolutionProposal` / `SelfEvolutionRisk` exists anywhere in the tree or in git history**.

Evidence:

- `git grep SelfEvolution HEAD -- '*.cs'` → only the 4 usage sites in the closed-loop neuron.
- `git log --all -S "record SelfEvolutionProposal"` → no commit ever defined it.
- Commit `5883759` ("remove out-of-scope untracked additions (self-evolution synapses, ...)")
  deleted the *untracked* file that defined these records, while the neuron that consumes them
  was already committed. The previous session's green verification predates that deletion.

Product impact: the *entire self-evolution approval rail* — the thing that makes autonomous
change safe — is currently a dangling reference. This is the first thing to fix, and it is also
the smallest safe slice: re-author the records in Core, matching the call site, and pin them
with a test so they cannot silently vanish again.

## 1. Self-evolution guardrails — product gap map

The product promise is "closed-loop self-evolution with human approval, journaling, rollback".
Today only the *proposal staging* half-exists (and is broken, §0). Ranked gaps:

| # | Gap | Where | Risk |
|---|-----|-------|------|
| 1 | Proposal record types missing → build broken | `SoftwareEngineeringClosedLoopNeuron.cs:87` | CRITICAL |
| 2 | No approval handler: nothing consumes a proposal, nothing enforces `RequiresHumanApproval` | (absent) | CRITICAL |
| 3 | Marketplace install → `NeuroPackInstalled` → `GeneratedNeuron` embodiment runs pack code with trust/signature checks but **no proposal/journal step** | `MarketplaceNeuron.cs:74–148`, `GeneratedNeuron.cs:47` | HIGH |
| 4 | `define_reaction` (MCP) → `AutomationNeuron` executes arbitrary Roslyn C# in-process, gated only by `CapabilityGate` | `AutomationNeuron.cs:75–120`, `DigitalBrainMutationTools.cs:88–109` | HIGH |
| 5 | Tier-1 `run_code_foundry` executes in-process (collectible ALC, not the OutOfProcessSandbox) with `autoApply=true` default | `CodeRunNeuron.cs`, `InProcessAlcExecutor.cs` | HIGH |
| 6 | Journals are in-memory prototypes; proposals/decisions would not survive kernel restart (no durable audit trail) | `PrototypeJournals.cs` | HIGH |
| 7 | Rollback plans are prose, not procedure: checkpoints are created but no `RestoreCheckpoint` executor exists | `CodeFoundryClosedLoopNeuron.cs`, `Core/Synapse.cs` (`Checkpoint`) | MEDIUM |
| 8 | No approval identity/expiry model (who approved, when, until when) | (absent) | MEDIUM |
| 9 | Zero tests pin the proposal→approval→apply→rollback contract | test suite | HIGH |

Product framing: gaps 1–2 are the "consent loop" (table stakes for a self-evolving OS people
will trust). Gaps 3–5 are the "side doors" — every mutation path must go through the same rail
or the rail is decorative. Gaps 6–8 are the "memory of decisions" — Ino's long-term-memory
story starts with the system remembering *its own* decisions durably.

## 2. Dead / trash code (proof status noted)

| Item | Evidence | Verdict |
|------|----------|---------|
| `DigitalBrain.Developer` + `.Tests` | zero `.cs` source files on disk; in `Brain.slnx` | **Delete** (proven empty) |
| `DigitalBrain.Windows` + `.Tests` | zero `.cs` source files on disk; in `Brain.slnx` | **Delete** (proven empty) |
| `SystemRollingSurfaces.cs` (82 loc) | referenced only from `CheckpointBackupTriggerTests` | Suspicious — verify rolling-update surface flow is truly unused before deleting |
| `PrototypeJournals.cs` | used only by `Program.cs` in same project | Not dead; candidate to inline or rename (§4), and to replace with durable journal (§1.6) |

## 3. Thin wrappers / project merges

| Project | Contents | Recommendation |
|---------|----------|----------------|
| `DigitalBrain.Telegram.Channel` | 1 interface (`ITelegramChatNeuron`, 8 loc) | Merge into `DigitalBrain.Telegram`; delete project + its 1-test project |
| `DigitalBrain.UiKit` | 1 interface (`IFlutterUiNeuron`, 7 loc) | Merge into `DigitalBrain.Ui.Contracts`; delete project + tests project |
| `DigitalBrain.Telegram` / `.Transport` | pack (2 files) / real webhook host | Keep both — legitimate boundaries |
| `DigitalBrain.Demo.Contracts` / `.Runtime` | live: gateway routes `SurfaceDemoRuntime.RequestType` | Keep (demo is a product surface today) |
| `DigitalBrain.Experience.PersonalAssistant` | pack composing Telegram+Context+LLM; distinct from Kernel/Ino | Keep — correct grain-vs-pack separation |

Note: Core/Synapse.cs comments explicitly justify Telegram.Channel/UiKit as "peer ino projects";
merging them means updating that comment and the architecture guard tests
(`CoreBoundaryTests`) — small but must be done together.

## 4. Naming vs the DigitalBrain/NeuroOS model

- "silo": all remaining hits are Orleans-technical (builder APIs, test collections). Clean.
- Neuron/synapse naming: consistent. Ino (kernel grain) vs PersonalAssistant (pack) is a
  correct, intentional split.
- `PrototypeJournals` / `ConfigurePrototypeJournals()`: name is *accurate today* (they are
  in-memory prototypes) — rename only when the durable journal lands (§1.6); renaming first
  would hide the real gap.
- Wire strings that must not change without versioning: `"DigitalBrain.Kernel.SurfaceDemoRequested"`
  (`SurfaceDemoRuntime.cs:9`), `DemoMessageSynapse`, `[GrainType("ino.personal.v1")]`.

## 5. Overgrown files (split candidates, in priority order)

1. `Kernel/Ino/InoNeuron.cs` (865 loc) — intent detection + Gmail/Salesforce/market/schema
   handlers + LLM reasoning + memory in one grain. Product-critical: this is Ino's brain, and
   every new user-facing capability lands here today. Seam: per-intent handlers behind a small
   classifier; grain stays the orchestrator. This is also the prerequisite for "Ino creates
   neurons on request" to be reviewable.
2. `Kernel/Gateway/GatewayService.cs` (554 loc) — `Send()` is a ~150-line if/else over 15+
   request types. Seam: handler-per-domain (marketplace, auth, config, demo, ino, telegram).
   Payload/auth helpers already extracted; continue the same pattern.
3. `Marketplace.Contracts/MarketplaceUiSurfaces.cs` (733 loc) — pack→row mapping + action
   generation (with Salesforce special cases) + surface templates. Extract action generator.
4. `Aspire/DigitalBrainBuilderExtensions.cs` (457 loc) — one mega-method wiring LLM, storage,
   Orleans, replicas, voice. Lower priority (infra, changes rarely).
5. `Ui.Runtime/UiSurfaceRuntime.cs` (909 loc) — big but cohesive sample-surface library. Defer.

## 6. Brittle tests

- **brittle-timing**: `GatewayServiceTests.cs:244–286` (poll loops + `Task.Delay(300)`),
  `LoginRendersE2ETests.cs:56` (`Task.Delay(750)`), `JournalFormatSpikeTests` (40 s poll — acceptable for a spike, quarantine from fast lane). Fix: shared `WaitUntilAsync` helper.
- **brittle-singleton**: `LlmResponderScopedConfigTests.cs:79,106` — `public static readonly ...Factory` with `.Clear()` between tests; safe only because parallelization is disabled. Fix: per-fixture instance or AsyncLocal.
- **brittle-ordering**: mostly fine — `.Single(predicate)` keyed lookups dominate; no exact-order asserts on unordered sets found.
- **brittle-string**: none found (no exact LLM-output asserts).

## 7. Prioritized action plan

| # | Slice | Why first | Size |
|---|-------|-----------|------|
| 1 | **Restore self-evolution rails**: define `SelfEvolutionProposal`, `SelfEvolutionRisk`, `SelfEvolutionDecision` in `DigitalBrain.Core/SelfEvolution.cs`; pin with a contract test | Unbreaks master; restores the approval vocabulary the product is built on | S |
| 2 | Delete empty `DigitalBrain.Developer`/`.Windows` (+ test shells) from disk and `Brain.slnx` | Proven-dead; shrinks solution; zero risk | S |
| 3 | Add an approval consumer: a neuron (or extend closed-loop) that journals proposals and gates `ApplyVia` on an explicit `SelfEvolutionDecision`; expiry field on proposal | Turns the rail from vocabulary into enforcement | M |
| 4 | Merge `Telegram.Channel` → `Telegram`, `UiKit` → `Ui.Contracts`; update Core comment + guard tests | Fewer projects, same boundaries | M |
| 5 | Route marketplace install + `define_reaction` + Tier-1 foundry through the proposal rail (config flag to keep dev loop fast) | Closes the side doors | M/L |
| 6 | Durable journal for proposals/decisions (replace in-memory prototype for the self-evolution stream first, not everything) | Audit trail that survives restart | M |
| 7 | Split `InoNeuron` into intent handlers | Product velocity + reviewability of Ino growth | M |
| 8 | Continue `GatewayService.Send()` handler extraction | Same pattern as prior session | M |
| 9 | De-flake timing tests with `WaitUntilAsync`; de-static LLM test factories | CI trust | S/M |
| 10 | Rollback executor for checkpoints (`RestoreCheckpoint`) + test | Completes propose→approve→apply→rollback | L |

Slice 1 was implemented in this session: `DigitalBrain.Core/SelfEvolution.cs` (proposal/decision/risk
records, additive only) and `DigitalBrain.Tests/Kernel/SelfEvolutionContractTests.cs` (2 tests pinning
the contract). Verified: Core, Kernel, and Tests build clean; SelfEvolution (2), Architecture (18),
UiSurfaceContract (30), and ChatClientRegistration (7) tests pass. Slices 2–3 are the recommended
next session; note slice 2 requires removing the `DigitalBrain.Developer`/`DigitalBrain.Windows`
ProjectReferences from `DigitalBrain.Kernel.csproj` and the two test-shell projects from `Brain.slnx`
in the same commit.
