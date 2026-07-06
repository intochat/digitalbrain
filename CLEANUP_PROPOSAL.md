# DigitalBrain / NeuroOS — Comprehensive Cleanup & Architecture Refinement Proposal

**Date:** 2026-07-06  
**Status:** DRAFT — FOR USER APPROVAL (Execution in progress)  
**Author:** Grok (analysis based on full repo scan + existing `ARCHITECTURE_CLEANUP_PROPOSAL.md`)  
**Goal:** Make requirements less dumb. **Delete** aggressively. Simplify boundaries. Accelerate the inner loop and the self-evolution cycle. Prepare the system for long-term extensibility as a true self-evolving AI-native OS built on Neurons, Synapses, and live C# Packs.

**Execution Progress (as of latest):** Phase 0 deletions largely complete per this proposal and the 2026-07-06-dead-neuron-cleanup plan:
- Dead Ino language editor, CompilerNeuron, Software20TeamNeuron, Awesome/ reviewer + ProjectReview fully pruned (synapses, grains, tests, features, dirs).
- .claude skills cache removed.
- Silo→kernel product renames (test collections "silo-host"→"kernel-host", configurator file/class, comments in Program.cs, docs).
- Dead SDK neurons (Roslyn/Git/NuGet/DotNet/FS/Shell/Winget in Kernel/Developer/Windows) + dead Google Drive/Calendar deleted, ProcessRunner relocated, fakes/registrations/configs/tests trimmed.
- Roslyn packages bumped to 5.6.0 unified (skew fixed, duplicate pin removed).
- Deploy project excluded from CI test graph (SkipDeployBuild).
- Docs updated for removals and naming.
- All with per-step `dotnet build` + targeted tests + `aspire doctor`; net deletions >> additions.
- Full baseline test count reduced as expected by deleted tests (see plan for details). Isolated live tests green.

> **Core principle (AGENTS.md):** Keep changes small and focused after approval. Delete more than we add. Run `dotnet build && dotnet test --filter "..."` + `aspire doctor` after every logical chunk. Use Aspire MCP tools for hosting changes.

---

## 1. Executive Summary

DigitalBrain is a powerful vision: an Aspire + Orleans runtime where **everything is a Neuron** (actor grain with causal journals, broadcast, checkpoints) or **Synapse** (immutable typed message), and behavior extends at runtime via signed **NeuroPacks** (C# compiled into collectible ALCs and embodied without restart).

The architecture has strong bones, but accumulated bloat, legacy naming, dead code paths, and monolithic project structure slow down iteration and obscure the self-evolution story.

**This proposal delivers a concrete, phased, delete-heavy cleanup plan** that:
- Removes clear trash (dead Ino language editor, superseded experiments).
- Finishes the Silo → Kernel rename for product concepts.
- Hardens boundaries so `DigitalBrain.Core` becomes true stable primitives.
- Breaks the Kernel monolith into logical modules (without a giant rewrite in one go).
- Moves "capability spikes" (Google, Salesforce, etc.) toward optional/pack-based future.
- Splits the giant test project for fast feedback.
- Reduces warning noise and cognitive load.

**Expected outcomes after full execution:**
- Smaller, clearer Core (primitives + minimal contracts).
- Kernel as composition root + focused runtime modules.
- Faster `dotnet test` loops.
- Easier onboarding and future pack authors.
- Stronger foundation for self-evolution (packs become the primary way to add neurons/synapses/closed loops).

**Current health (verified 2026-07-06):**
- `dotnet build Brain.slnx -p:SkipFlutterBuild=true` → 0 errors.
- `aspire doctor` → 5/5 pass.
- Core paths (broadcast, pack embodiment, neurons) → 58 passed / 0 failed (E2E skips expected).

---

## 2. Vision: The Target Architecture for a Self-Evolving System

A self-evolving system must make **adding new behavior cheap and safe** and **removing dead weight automatic**.

**Target principles:**
- **Primitives are tiny and stable.** Packs, the marketplace, and the kernel itself only depend on the smallest possible contract set.
- **Kernel is a host, not a feature dump.** Composition root + thin runtime pieces. New neurons mostly arrive via packs.
- **Integrations are capabilities, not compile-time requirements.** Google/Salesforce/etc. should be installable experiences, not mandatory references.
- **Tests give fast feedback.** Separate lanes: pure contracts, runtime (TestCluster), pack authoring, E2E (Aspire).
- **Names are consistent.** "Kernel" for the product runtime. Orleans technical terms stay where accurate.
- **Dead code has a short half-life.** Explicitly marked or deleted.

**Proposed high-level structure (future state):**

```
brain/
├── DigitalBrain.Core/                  # MINIMAL: INeuron, Synapse base, ids, causality, core task/system contracts
├── DigitalBrain.Primitives/            # (future split) pure records + no Orleans
├── DigitalBrain.Runtime.Contracts/     # INeuron + IHandle, checkpointing
├── DigitalBrain.Pack.Contracts/
├── DigitalBrain.Marketplace.Contracts/
├── DigitalBrain.Ui.Contracts/
├── DigitalBrain.Ui.Runtime/
├── DigitalBrain.Demo.Contracts/
├── DigitalBrain.Demo.Runtime/
├── DigitalBrain.Kernel/
│   ├── Neuron.cs, SynapseDispatch.cs, SynapseStream.cs   # base
│   ├── Kernel/ (services, journals, checkpoints)
│   ├── Foundry/ (PackAlcEmbodier + closed loops)          # extracted module
│   ├── Gateway/ (gRPC + resolver)
│   ├── Marketplace/
│   ├── Llm/
│   ├── Ui/
│   └── Program.cs + DigitalBrainKernelExtensions.cs      # thin composition
├── integrations/
│   ├── DigitalBrain.Context/
│   ├── DigitalBrain.Google/          # (future: more pack-oriented or opt-in)
│   └── ...
├── hosts/
│   ├── DigitalBrain.AppHost/
│   ├── DigitalBrain.ServiceDefaults/
│   └── DigitalBrain.Telegram.Transport/
├── tests/ (split lanes)
├── deploy/
└── app/ (Flutter)
```

Packs become the dominant way to ship new neurons, surfaces, automations, and even new "closed loops".

---

## 3. Current State Inventory (2026-07-06)

### 3.1 Solution Shape (`Brain.slnx`)
- `/src/`: Core contracts + Kernel + Aspire + Mcp + Pack/Marketplace/Ui/Demo contracts + SeedPacks
- `/integrations/`: Context, Developer, Experience.PersonalAssistant, Google, Salesforce, Telegram.*, UiKit, Windows
- `/hosts/`: AppHost, ServiceDefaults, Telegram.Transport
- `/tests/`: Many *Tests projects + giant `DigitalBrain.Tests`
- `/deploy/`: Pulumi
- `/clients/`: `app/Flutter.proj`

### 3.2 Kernel Source Files (clean list, excluding bin/obj/Generated)
(See tool output for full 80+ files. Notable concentrations:)
- Heavy: `Gateway/`, `Foundry/`, `Ui/`, `Economics/`, `Llm/`, `Sync/`, integration folders inside Kernel (`Google/`, `Salesforce/`, `Sdk/`)
- Legacy experiments: `InoCodeEditorNeuron.cs`, `CompilerNeuron.cs`, `Software20TeamNeuron.cs`, `Awesome/`
- Personal assistant (keep): `Ino/InoNeuron.cs`

### 3.3 Biggest Architectural Problems
1. `DigitalBrain.Core/Synapse.cs` is a kitchen sink (~350+ lines of unrelated domain records).
2. `DigitalBrain.Kernel.csproj` references almost everything → monolith.
3. Dead "ino" language tooling still wired in multiple places (MCP, gateway, program warmup, dedicated grain).
4. Product "silo" naming leaks.
5. Test surface is one giant project + many narrow ones.
6. Some closed-loop / software-2.0 paths build packs but never embody them (CompilerNeuron).

---

## 4. What Is Core? (Non-Negotiable — Protect & Minimize)

**Must stay small and extremely stable:**
- `INeuron`, `IHandle<T>`, `Synapse` base + `Stamp`, `NeuronId`, `TaskId`, `Checkpoint`, branch/restore, causal queries.
- Core system messages needed for the kernel to function standalone: `StartDistributedApp`, `RestartResource`, basic task lifecycle, `SystemLaunched`/`SystemStatusChanged`, `Login*` family (for air-gapped scenarios).
- `NeuroPack` / pack embodiment contracts (already moved to `Pack.Contracts` — good).
- UI contracts (already moved — good).

**Currently polluting Core (candidates to split out):**
- All the Ino* (language + assistant)
- Software engineering team / closed loop specific records
- NuGet / Architect / Db* / Tabular / many others
- `LlmPrompt` / `LlmResponse` (could live in Llm contracts later)
- Full auth/user profile if marketplace takes over

---

## 5. Trash Inventory — Precise, Actionable Catalog

### 5.1 Dead "Ino" Language / Code Editor (Highest priority delete)

**Reason:** README states "typed C# only (.ino is dead)". This was the old visual editor for a non-C# language. The personal assistant neuron (`IInoNeuron` + `InoRequest`) is a **different, living** ultra-context feature.

**Exact items to delete / remove wiring:**

| Location | Item | References / Impact |
|----------|------|---------------------|
| `DigitalBrain.Core/Synapse.cs` | `InoCodeEdit`, `InoCodeRun`, `InoCodeSave`, `InoCodeExecute`, `InoCodeApplySkill`, `IInoCodeEditor` interface + comment | Core contracts |
| `DigitalBrain.Kernel/InoCodeEditorNeuron.cs` | Entire file (~60 LOC) | Grain implementation |
| `DigitalBrain.Kernel/Gateway/NeuronResolver.cs:29` | `"ino-editor-main" => ...` case | Resolver |
| `DigitalBrain.Kernel/Program.cs:423` | Warmup `GetGrain<IInoCodeEditor>("ino-editor-main")` | Host startup |
| `DigitalBrain.Mcp/DigitalBrainToolsBase.cs:80` | `"ino-editor-main"` resolver entry | MCP tool discovery |
| `DigitalBrain.Mcp/DigitalBrainMutationTools.cs` | `InoCodeEditor` tool + description + handling | `ino_code_editor` MCP tool |
| `DigitalBrain.Kernel/Ino/InoNeuron.cs:611` | Query for `OfType<InoCodeEdit>()` in journal (for context) | Minor usage inside living InoNeuron — clean or guard |
| `SoftwareEngineeringClosedLoopNeuron.cs` | Prompt text mentioning "InoCodeEditor" | Just update the example prompt |
| Archive docs | Historical mentions | Leave or lightly update |

**Living Ino assistant (DO NOT DELETE):**
- `InoRequest`, `InoResponse`, `IInoNeuron`, `MemorySummary`
- `Ino/InoNeuron.cs`
- All `"ino-main"` wiring and `AskAsync`
- MCP "ask_ino" paths
- UI surfaces that use it

**Verification after removal:** `dotnet test --filter "Ino"` should still pass for the assistant paths. No compile errors on `IInoCodeEditor`.

### 5.2 CompilerNeuron & Related Dead-ish Paths

- `DigitalBrain.Kernel/CompilerNeuron.cs` — implements `ICompiler` / `CreateNeuronRequest`. Generates snippet + `NeuroPack` object but **never publishes or embodies it** (per archive analysis). Superseded by proper Foundry + Marketplace + GeneratedNeuron.
- `ICompiler` interface in Core.
- Related test steps in `DigitalBrain.Tests/Steps/NeuronSteps.cs`.

**Recommendation:** Delete or heavily deprecate the `CompilerNeuron` + `ICompiler` + `CreateNeuronRequest`/`NeuronCodeGenerated` if no live path uses the output to produce a runnable pack. Keep the concept of "LLM can propose code" inside proper foundry neurons.

### 5.3 Software 2.0 / Awesome Experiments (Review & Prune)

- `Software20TeamNeuron.cs` + `ISoftware20Team` + `CreateSimpleApp` / `SimpleAppCreated` synapses.
- `Awesome/` folder + `SoftwareEngineeringReviewerNeuron`.
- `Core/Awesome/ReviewSynapses.cs`

These were early experiments. Some closed-loop neurons (`SoftwareEngineeringClosedLoopNeuron`, `CodeFoundryClosedLoopNeuron`) are more evolved. Proposal: keep the good closed-loop neurons; delete or move the old team neuron experiments into `docs/archive` or a demo pack.

### 5.4 Product "Silo" Naming Debt (Mechanical Rename)

**Keep (Orleans technical):**
- `ConfigureSilo`, `ISiloBuilder`, `SiloAddress`, `InProcessSiloHandle`, `GetSiloIdentityAsync()` (diagnostic), "cross-silo" test terminology, stream comments.

**Change (product / operational):**
- Calls and defaults that use `RestartResource("silo")` → `"kernel"`
- Any Docker/container repo comments still saying "silo"
- `WireKernelSilo` (method name — consider keeping for now or adding `WireKernel` alias)
- Deploy workflow comments, Pulumi descriptions
- `KernelRestartRequested` is already good
- `INeuron.cs` comment about "silo"
- Test names that are product-oriented (HomeFeedCrossSiloTests can stay as "cross-silo" is accurate for Orleans)

**Exact places (from scans):**
- `DigitalBrain.Core/INeuron.cs:22-24`
- `RestartResource` usages (MCP, MarketplaceUiSurfaces, AppHost wiring)
- Deploy/ + .github/workflows/
- Various docs and demo scripts (update comments)

### 5.5 Integration Bloat (Long-term Extract / Opt-in)

Current Kernel references (from csproj):
- DigitalBrain.Context, .Developer, .Google, .Salesforce, .Telegram.Channel, .UiKit, .Windows, .ServiceDefaults + Demo + all contracts.

**Proposal classification:**
- **Core substrate** (keep close): Context (memory), UiKit (for surfaces)
- **Capability spikes** (move toward packs or explicit opt-in feature flags): Google, Salesforce, Developer (Roslyn etc.), Windows, PersonalAssistant experience, much of Telegram chat neuron logic.
- Telegram.Transport host is a real separate deployable — good.

Future: Many of these should be published as first-party marketplace packs so a minimal kernel image can still gain the capability at runtime.

### 5.6 Test Debt

- `DigitalBrain.Tests` — ~10k+ LOC, mixes everything.
- Duplicate narrow test projects.
- Many E2E that are skipped in fast runs.

**Plan:** Create explicit solution filters or sub-projects:
- Unit / contract tests
- Runtime (Orleans TestCluster)
- Pack & Embodiment
- Integration (per domain)
- E2E (Aspire + full stack)

### 5.7 Other Low-Hanging Trash / Polish

- `PrototypeJournals.cs` + `TestKit` equivalents (obsolete journal shims).
- Duplicate CodeAnalysis package versions causing NU1608 warnings (Directory.Packages.props).
- Obsolete Azure storage configuration calls in `Program.cs`.
- Nullability warnings in tests and prototype code.
- Stale specs/plans in `docs/superpowers/` (follow the "delete after merge or mark active" rule).
- Any remaining embedded demo seeds that belong in `SeedPacks`.

---

## 6. Phased Execution Plan (Delete-Heavy, Verifiable)

**Rule for all phases:** After any edit batch:
1. `dotnet build Brain.slnx -p:SkipFlutterBuild=true`
2. `dotnet test ... --filter "relevant"` (fast lane first)
3. `aspire doctor`
4. (When relevant) targeted resource restart via aspire MCP + log check.

### Phase 0 — Deletion & Rename Pass (Safest, Highest Signal)

**Goal:** Remove obvious trash + finish naming. No behavior change for living features.

**Step 0.1 — Dead Ino language editor removal**
- Delete the 5 records + interface from `Synapse.cs`
- Delete `InoCodeEditorNeuron.cs`
- Remove cases from `NeuronResolver.cs`, `Program.cs`, `DigitalBrainToolsBase.cs`
- Remove the MCP tool method + registration in `DigitalBrainMutationTools.cs`
- Clean journal query in `InoNeuron.cs` (or make it ignore the old types gracefully)
- Update the one prompt in `SoftwareEngineeringClosedLoopNeuron.cs`
- Update any tests that directly reference the editor (rare)

**Verification filter examples:**
- `dotnet test --filter "FullyQualifiedName~Ino"` (assistant paths must still work)
- `dotnet test --filter "FullyQualifiedName~PackEmbod|Broadcast"`

**Step 0.2 — CompilerNeuron deprecation / removal**
- Delete `CompilerNeuron.cs`
- Remove `ICompiler` and related synapses from Core if unused in live paths
- Update `NeuronResolver`, `NeuronSteps.cs`, MCP if needed
- Decision gate: search for any call that actually consumes `NeuronCodeGenerated` to produce an embodied pack.

**Step 0.3 — Silo → kernel product renames**
- Change example/default `RestartResource("silo")` → `"kernel"` in code, tests, docs, MCP, AppHost wiring.
- Update comments in `INeuron.cs`, `Program.cs`, deploy files.
- Leave `GetSiloIdentityAsync()` for now (add `[Obsolete]` + better name later if desired).
- Update `rg` expectation in ARCHITECTURE_CLEANUP_PROPOSAL.md

**Step 0.4 — Quick wins**
- Fix CodeAnalysis version skew in `Directory.Packages.props` (align scripting & workspaces or document why split).
- Clean obsolete Azure client config calls.
- Delete or move `PrototypeJournals.cs` if no longer needed.
- Tidy obvious nullability in hot paths (optional).

**Phase 0 exit criteria:** Build clean (fewer warnings), key tests green, no more "ino-editor-main" or dead InoCode* references in runtime paths.

### Phase 1 — Boundary Hardening (No Big Moves)

- Add / enforce architecture tests (if missing) that prevent Core from taking unwanted dependencies.
- Rule: New feature code goes into a subfolder with registration extension, never root of Kernel.
- Extract one service registration extension as example (e.g. `AddFoundryServices`).
- Move demo fallback behavior out of gateway where possible (already partially done).

### Phase 2 — Core Split (Medium)

Split `DigitalBrain.Core` along stability lines:
- `DigitalBrain.Primitives`
- `DigitalBrain.Runtime.Contracts`
- Keep domain-specific things in their contract packages or move to new ones.

Use type forwarding for one release if packaging requires it.

### Phase 3 — Kernel Modularization (Larger but Incremental)

Create internal modules or separate assemblies:
- `DigitalBrain.Kernel.Runtime`
- `DigitalBrain.Kernel.Foundry`
- `DigitalBrain.Kernel.Gateway`
- etc.

Or start with folders + `internal` + clear extension methods.

### Phase 4 — Integration & Test Strategy

- Mark integration projects as "capability modules".
- Create fast test solution filters or `.slnf` files.
- Move expensive E2E behind explicit tags.

### Phase 5 — Polish & Extensibility

- Strengthen `CapabilityGate`.
- Improve pack authoring test loop.
- Document "how to add a new built-in neuron vs ship it as a pack".
- Final naming audit.

---

## 7. Detailed Deletion & Change Checklists (for Execution)

### Ino Language Editor Deletion Checklist
- [ ] `DigitalBrain.Core/Synapse.cs` — remove InoCode* + IInoCodeEditor
- [ ] Delete `DigitalBrain.Kernel/InoCodeEditorNeuron.cs`
- [ ] Edit `NeuronResolver.cs`
- [ ] Edit `Program.cs` (warmup + comments)
- [ ] Edit MCP files
- [ ] Edit `InoNeuron.cs` journal query
- [ ] Edit closed-loop neuron prompt
- [ ] Search whole repo for remaining references
- [ ] Run full relevant test matrix

### Silo Rename Checklist (product)
- [ ] `RestartResource` call sites and docs
- [ ] Deploy / workflow / Pulumi comments and names (where safe)
- [ ] `INeuron.cs` comment
- [ ] Marketplace UI surfaces examples
- [ ] Any hard-coded "silo" resource waits in non-technical tests

---

## 8. Risk & Rollback

- **Risk:** Accidentally breaking the living Ino personal assistant.  
  **Mitigation:** Extremely clear distinction in this doc + narrow test filters + keep all "ino-main" paths untouched.

- **Risk:** Removing CompilerNeuron breaks some MCP "run_closed_loop" or demo.  
  **Mitigation:** Search for consumers first. Keep the interface temporarily with `[Obsolete]`.

- **Risk:** Big refactors regress broadcast / pack embodiment.  
  **Mitigation:** Run the exact smoke filters after every batch. These are already proven green.

- Rollback: Git is our friend. Small commits = easy revert.

---

## 9. How This Improves Self-Evolution & Extensibility

- Dead editor code no longer pollutes the primitive contract surface that packs depend on.
- Clearer boundaries = safer pack authors (they see only what they need).
- Faster tests = more iterations on the embodiment and closed-loop code.
- Kernel becomes a better host for packs instead of a competing source of neurons.
- Consistent naming reduces confusion when people write new packs that talk about "restarting the kernel".

---

## 10. Verification Matrix (Run After Every Phase)

| Check | Command / Tool |
|-------|----------------|
| Build | `dotnet build Brain.slnx -p:SkipFlutterBuild=true` |
| Fast tests | `dotnet test --filter "FullyQualifiedName~Broadcast|PackAlcEmbodier|NeuronTests|Generated|Ino"` |
| Doctor | `aspire doctor` (MCP or CLI) |
| Architecture guards | (future) dedicated test |
| Relevant E2E (before PR) | Full `aspire run` + manual or scripted pack install + surface check |
| Naming | `rg -n "ino-editor|InoCodeEditor|ino.code.editor" --glob '!archive/**'` (must be 0) |

---

## 11. Approval Section

**This document is the single source of truth for the next wave of cleanup.**

**To approve:**
Reply with **APPROVED** (optionally + any specific constraints or order changes).

**To request changes:**
List the sections or items you want adjusted (e.g., "keep CompilerNeuron for now", "do not touch GetSiloIdentityAsync", "add X to Phase 0").

Once approved, execution will proceed **phase by phase**, with explicit confirmation points between major phases if desired. All changes will be small, tested, and reversible.

**Current recommendation for first execution slice after approval:**
Phase 0.1 (Dead Ino language editor) + Phase 0.3 (product silo renames) + quick package warning fixes.

---

## Appendix A — Raw Supporting Data (Captured 2026-07-06)

- Kernel source .cs files: (see tool output in session — 80+ files listed above)
- Ino language symbols: InoCodeEdit/Run/Save/Execute/ApplySkill + IInoCodeEditor
- Living Ino assistant symbols: InoRequest, InoResponse, IInoNeuron + Ino/InoNeuron.cs
- Key monolith references: listed in `DigitalBrain.Kernel.csproj`
- Build warnings observed: NU1608 CodeAnalysis skew, CS0618 obsolete Azure config, various nullable in tests/prototypes.

---

## Appendix B — References to Prior Work

- `ARCHITECTURE_CLEANUP_PROPOSAL.md` (2026-07-03) — many items already actioned.
- `docs/SYSTEM_DESIGN.md`
- `docs/archive/CONTINUATION-CLEANUP-SIMPLIFICATION.md`
- AGENTS.md (5-step process)

---

**End of Proposal.**

Ready for review and explicit approval.