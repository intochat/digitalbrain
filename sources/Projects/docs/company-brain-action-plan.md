# Company Brain — Ruthless Action Plan (NeuroOS)

**Goal:** Realize the primitive described in `transcript.txt`: pull fragmented company domain knowledge (heads, threads, tickets, DBs), structure it, keep it current, and turn it into executable skills that AI agents (and the system) run safely and consistently. Not a search/chat layer — a living executable map.

**Inputs:** `transcript.txt`, `Projects/docs/projects-survey-comparison.md`, existing `brain/` substrate (post best-of-breed consolidation per `CONTINUITY.md`), `brain/README.md`.

**Method:** Applied Elon's 5 Steps strictly in order. Every item below is the result of delete-first thinking. No "just in case", no unrelated polish, no secondary concerns (global economics, WASM, broad client UI, full source connectors, general self-improvement theater).

## 5 Steps Application (summary)

1. **Make requirements less dumb**
   - Primary requirement (transcript): knowledge → structured → current → *executable* skills for real work (refunds, pricing exceptions, incidents).
   - "Skills file" = `IPackBehavior` (typed C# pack) — already the chosen executable primitive (see `DigitalBrain.Core/Distribution/IPackBehavior.cs`, `GeneratedNeuron`).
   - Challenge: "Build a full multi-tenant global marketplace with payments first" is dumb for company brain. Per-company private brain + internal skill registry is the 80%. Payments/global sharing is a later market layer.
   - "Ingest everything on day 1" is dumb. High-signal process sources (playbooks + transcripts) first.
   - "Make it a general code foundry for any C#" is dumb when the bottleneck is *company process crystallization into safe executable behavior*.
   - Traceability: transcript primary; survey gap analysis (end-to-end typed pack embody + dispatch) secondary constraint.

2. **Delete (aggressively)**
   - Global Stripe/economics/license-gated marketplace for MVP.
   - WASM / out-of-proc sandbox (CapabilityGate + journals + branching is sufficient initial safety).
   - Full Flutter/SDUI/RFW client work and HomeFeedBus as prerequisite.
   - Broad "awesome" seeding and unrelated closed loops.
   - INO as behavior language for skills (typed C# only).
   - General-purpose foundry scope creep; narrow to process skill synthesis.
   - "Support Slack + email + Jira + DBs + live scraping" connectors.
   - "Self-improving everything" before a single company process skill works end-to-end.
   - Per-pack grain types (use single `GeneratedNeuron` host + `IPackBehavior` capability object).

3. **Simplify (what remains)**
   - One skill abstraction: `IPackBehavior` that implements one narrow company process. `CanHandle`/`Handle` specific `Synapse` types (or `ExperienceUsed` compat). Pure logic. Emits `PackEmission` + outcome `Synapse`s. Side effects modeled as emitted command synapses (handled by SDK neurons or caller).
   - One canonical vertical: "Refund handling" skill (directly from transcript).
   - Ingestion: file drop / paste of process playbooks + transcripts + sample artifacts (JSON/text). Use/extend `DocumentIngestor` + `ContextNeuron`.
   - Crystallize: `ContextNeuron.Recall` + LLM → structured `ProcessSpec` (steps, decisions, exceptions, required inputs, emitted outcomes).
   - Synthesize: specialized prompt + code template → compilable, gate-passing `IPackBehavior` C# using existing `FoundryCompilation` + `PackAlcEmbodier`.
   - Embodiment & dispatch: reuse `PackAlcEmbodier` → `GeneratedNeuron` (key: `TryEmbody`, `TryDispatchEmbodiedAsync`).
   - Living map & safety: 100% dual journals (`NeuronJournals`), `SynapseId` + `CausationId` + `CorrelationId`, `Checkpoint`/`Branch` for safe evolution.
   - Keep current: re-ingest source → diff → new version pack → verify → `InstallFromMarketplace` equivalent.
   - Invocation: MCP tool (internal) + direct grain fire for agents. `IContextNeuron` + skill grains.

4. **Accelerate cycle time** (only after 1-3)
   - Fast unit path: direct `PackAlcEmbodier.Embody` + `EmbodiedPack.Handle` in tests (no silo).
   - Fast integration: in-proc test cluster (see existing `DigitalBrain.Tests` patterns) or `start.cs` REPL + targeted grains.
   - Synthesis → embody → execute → journal inspect loop must be < minutes.
   - Use existing Reqnroll distribution harness patterns only for the new skill vertical.

5. **Automate** (last)
   - Only after the manual "ingest one playbook → crystallized spec → synthesized pack → embodied + executed + verified" vertical is green and understandable.
   - Then: closed loop that watches context changes for a process source, proposes vN+1 pack, runs verification, installs.

## Core Bottlenecks (only these matter)

1. No dedicated pipeline that turns raw ingested company knowledge into a precise, actionable `ProcessSpec`.
2. No reliable synthesizer that turns `ProcessSpec` into a minimal, correct, `CapabilityGate`-clean `IPackBehavior` that uses journals/causation properly and emits the right outcomes.
3. No end-to-end proof for a transcript-cited process (ingest → skill → embody via `PackAlcEmbodier`/`GeneratedNeuron` → fire relevant synapse → causal journal is the living map + proof it did the work).
4. No mechanism to keep a skill current when source knowledge drifts (re-crystallize, re-synth, safe re-embody).
5. Weak surface for "AI agents use these skills" (MCP is the right narrow door).

Everything else is noise until these are closed.

## Leveraged Assets (exact — extend these, do not duplicate)

**Core primitives (DigitalBrain.Core)**
- `Synapse` (with `SynapseId`, `CausationId`, `CorrelationId`, `Stamp`) — `brain/DigitalBrain.Core/Synapse.cs`
- `NeuroPack`, `NeuroPackInstalled`, `PackEmission`, `ExperienceUsed` — same file + `IPackBehavior.cs`
- `IPackBehavior` (Respond + CanHandle/Handle) — `brain/DigitalBrain.Core/Distribution/IPackBehavior.cs`
- `INeuron` + journal patterns via `Neuron`

**Embodiment (the keystone, recently landed)**
- `PackAlcEmbodier`, `EmbodiedPack`, `IPackEmbodiment`, `CapabilityGate`, `FoundryCompilation` — `brain/DigitalBrain.Silo/Foundry/`
- `GeneratedNeuron.TryEmbody` / `TryDispatchEmbodiedAsync` (handles `NeuroPackInstalled`, dispatches, emits `PackEmission`) — `brain/DigitalBrain.Silo/SystemNeurons.cs`
- In-proc executor + ALC unload patterns — `InProcessAlcExecutor`, tests in `DigitalBrain.Tests/Foundry/PackAlcEmbodierTests.cs`

**Knowledge / Context**
- `DocumentIngestor` + `TextChunker` — `brain/DigitalBrain.Silo/Context/DocumentIngestor.cs`
- `IContextNeuron` (`RememberAsync`, `RecallAsync`, journaled `MemoryStored`, hybrid score) — `brain/DigitalBrain.Silo/SystemNeurons.cs`, `Synapse.cs`
- `ContextServices.AddContextStore` (in-mem or Qdrant) — `brain/DigitalBrain.Silo/Context/ContextServices.cs`
- `NoOpEmbeddingGenerator` (swap for real later)

**Code synthesis**
- `CodeGenNeuron`, `FoundryRequest` etc. — `brain/DigitalBrain.Silo/Foundry/CodeGenNeuron.cs`, `CodeFoundrySynapses.cs`
- Existing LLM usage via `IChatClient`

**Market / install flow (adapt, do not expand)**
- `IMarketplaceNeuron`, `PublishToMarketplace`, `InstallFromMarketplace`, `SystemNeurons.cs` publish/install paths that deliver to `GeneratedNeuron`
- `start.cs` marketplace commands and seeds (use as test harness patterns only)

**Safety / evolution**
- Kernel checkpoint/branch/fork — continuity notes + `Kernel/`
- Dual journals on every neuron (append-only causal record = the map)
- `NeuronJournals`

**SDK tools (for skills to cause real actions)**
- `ShellNeuron`/`ProcessRunner`, `GitNeuron`, `FileSystemNeuron`, `DotNetNeuron` etc. — `brain/DigitalBrain.Silo/Sdk/`
- Use by emitting command synapses; the skill itself stays pure.

**Tests & harness**
- Cluster test patterns, `NeuronSteps`, `Pack*Tests`, Reqnroll distribution scenarios (harvest from `final` patterns via survey where needed, but keep typed-C# only).
- Direct `PackAlcEmbodier` unit tests for fast loop.

**Runtime**
- Aspire silo + 3 replicas default, MCP (internal), Gateway.

## Detailed Action Plan — Phases (core only)

Execute in order. Verify after each phase before next. Use latest packages (see `brain/Directory.Packages.props`). Run tests with high signal filters. Use relative paths.

### Phase 0: Baseline Validation (1-2 hours)

1. In `brain/`: `dotnet build` (Release). Fix any clean build issues.
2. Run embodiment + context + core dispatch tests:
   `dotnet test --filter "FullyQualifiedName~PackAlcEmbodier|GeneratedNeuron|ContextRecall|Install|PackEmission" -c Release --logger "console;verbosity=detailed"`
3. (Optional full substrate) From `brain/`: use aspire MCP `doctor` + targeted resource check if needed. Do not start full stack unless a test requires it.
4. In REPL (`dotnet run start.cs` or equivalent) or test: confirm a simple `NeuroPack` with `IPackBehavior` can be installed and `PackEmission` appears with real compiled output (not LLM fallback). Use existing `TypedDispatchPack` test pattern.
5. Success: all targeted tests green + one manual `PackEmission` with non-fallback output visible in journal.

Delete any assumption that "it works" without this evidence.

### Phase 1: Minimal Company Source Ingestion (focus bottleneck 1 + 5)

Goal: Get real process knowledge (playbook + transcript) into context where `Recall` + LLM can use it.

1. Create `brain/DigitalBrain.Silo/Company/` (new, minimal).
2. Add `CompanySourceIngestor` (thin wrapper over `DocumentIngestor` + direct `IContextNeuron.RememberAsync` for high-signal fragments). Support:
   - Directory of `.md`/`.txt` playbooks.
   - Raw transcript paste or file (interview notes describing "how we do refunds").
3. Add two canonical example sources under `samples/CompanyBrain/` (checked in):
   - `refund-policy.md` (clear steps, decision points, exceptions, outputs).
   - `refund-transcript.txt` (human describing the process).
4. Add synapses: `IngestCompanySource(string Collection, string SourceId, string Text)`, `CompanyProcessIngested`.
5. Wire a `CompanyKnowledgeNeuron` (or extend `ContextNeuron`) that ingests and also stores lightweight metadata (sourceId, version/timestamp).
6. Add `RecallCompanyKnowledge(string queryForProcess)` that combines vector recall + journaled memories.
7. Test: `dotnet test --filter "CompanyKnowledge|IngestCompanySource"` — assert chunks recalled contain key decision language from the example sources.
8. Verification command in plan: after ingest, call `RecallAsync("how do we decide refund eligibility")` and confirm relevant policy text returns.

Keep sources as plain text. No PDF. No live Slack connector.

### Phase 2: Crystallize ProcessSpec (bottleneck 1)

Goal: From recalled knowledge → structured spec that synthesis can consume deterministically.

1. Define minimal `ProcessSpec` record (in Core or Silo, narrow):
   - Name (e.g. "RefundHandling")
   - TriggerSynapseTypes
   - Steps (ordered decisions + actions)
   - DecisionPoints (inputs → conditions → branches)
   - ExceptionPaths
   - EmittedOutcomeTypes (what the skill fires on success/fail)
   - RequiredToolCapabilities (e.g. "can emit RefundApproved command")
2. Implement `ProcessCrystallizer` (service or neuron method) using:
   - `IContextNeuron.RecallAsync("refund process...")`
   - `IChatClient` with tight system prompt: "From the provided excerpts only, output ONLY a JSON ProcessSpec for the named process. Be precise on decision criteria."
   - Fallback deterministic parser for the example sources.
3. Add command synapse `CrystallizeProcess(string ProcessName, string[] SourceQueries)`.
4. Output `ProcessSpecCrystallized(ProcessSpec Spec, string[] EvidenceRefs)`.
5. Unit test the crystallizer against the checked-in examples. Assert key decision rules are captured (e.g. "if within 30 days and receipt present → eligible").
6. Verification: run crystallize on "RefundHandling" → inspect produced spec (in test assertion or console).

This stage must be inspectable and versioned with the source.

### Phase 3: Synthesize Executable Skill Pack (bottleneck 2 — the hardest)

Goal: `ProcessSpec` → safe, minimal, compilable `IPackBehavior` C# source that passes `CapabilityGate` and embodies the process.

1. Create `SkillPackSynthesizer` (in Foundry or new Company/ folder).
2. Define a small set of canonical process synapse contracts for the vertical (in a new `CompanySynapses.cs` or Core):
   - `RefundRequested { RequestId, Amount, Reason, CustomerId, ... }`
   - `RefundEligibilityChecked`, `RefundDecisionMade`, `RefundExecuted`, `RefundDenied` (or general outcome + specific).
   Keep to 4-6 for the first skill.
3. Build prompt + template:
   - System: "You are a precise C# generator for `IPackBehavior`. Output ONE self-contained public sealed class implementing `DigitalBrain.Core.IPackBehavior`. Use only the Handle/CanHandle path for the listed trigger synapses. Use pure logic + decision tables. Emit only `PackEmission` and the defined outcome synapses. Never call banned APIs. Include the exact namespace and usings shown."
   - Provide the `ProcessSpec` + example good pack (from tests) + bad examples to avoid.
4. Integrate with existing `FoundryCompilation.CreateWith` + run `CapabilityGate` inside the synthesizer (fail fast).
5. Produce `SkillPackSourceGenerated(string PackName, string Version, string Code)`.
6. Add a narrow pack template helper that always produces correct `CanHandle` for the domain synapses + `Handle` that routes and emits causal `PackEmission` with correct pack name.
7. Fast test loop: synthesizer unit tests that generate → `PackAlcEmbodier.Embody` → `Handle(new RefundRequested(...))` → assert emitted `PackEmission` + outcome synapses have correct data and causation lineage.
8. Success criteria: generated pack for RefundHandling passes gate, embodies, and on trigger synapse produces 1+ `PackEmission` + 1+ outcome with correct values. No LLM fallback path taken.

This is the core bottleneck closer. Iterate the prompt + template here until 95%+ reliable on the canonical examples (no "just add more LLM magic").

### Phase 4: End-to-End Skill Vertical — Ingest to Living Map (bottlenecks 3 + 5)

Goal: one command/path that proves the full transcript promise for one process.

1. Add `CreateCompanySkill(string ProcessName)` orchestration (in a `CompanySkillNeuron` or via MCP tool + existing foundry/market flow):
   - Ingest known sources for the process.
   - Crystallize spec.
   - Synthesize pack code.
   - (Optional) local verify: embody + fire representative triggers + assert journals (fast path, no full cluster).
   - Package as `NeuroPack` (Name="RefundHandling", Version, Code=generated, Owner=internal).
   - Deliver via existing marketplace/install path to `GeneratedNeuron` keyed e.g. "skill-refundhandling".
2. Add representative trigger firing + outcome verification in tests:
   - Fire `RefundRequested` (or `DemoMessageSynapse` + domain one) to the skill grain.
   - Wait for journal.
   - Assert causal chain: incoming trigger → embodied dispatch → `PackEmission`(s) → outcome synapse(s).
   - The journal *is* the living map entry for this execution.
3. Expose via MCP (internal only): tool `create_company_skill(processName)`, `invoke_skill(skillName, synapseJson)`.
   - See existing `DigitalBrain.Mcp.Tools` patterns. Add in `DigitalBrainTools.Neurons.cs` style.
4. Manual demo path (in `start.cs` or dedicated script):
   - `company-skill refund`
   - Then fire a trigger.
   - Dump relevant portion of `generated-skill-refundhandling` incoming/outgoing journals.
5. Verification commands:
   - `dotnet test --filter "CompanySkill|Refund|EndToEndSkill"` (must pass with real embodied emissions).
   - In running kernel (if used): use MCP or REPL to list journals and confirm `PackEmission` with synthesized logic output.

At end of this phase: "ingest playbook for refunds → skill exists and is executable → firing the right synapse runs the company logic and writes the audit map" is demonstrable and repeatable.

### Phase 5: Keep Current (bottleneck 4)

Goal: when source changes, skill can be safely updated without losing history.

1. Store source digest (hash of ingested text) with the skill version.
2. On re-ingest of same collection: detect delta (simple text diff or re-crystallize and compare specs).
3. On detected drift: run full Phase 4 synthesis for vNext.
4. Use kernel `Checkpoint` before attempting update.
5. Install new version to same skill grain (or new "skill-refundhandling-v2" with alias). `GeneratedNeuron` already handles replace via `_embodied` swap + deactivate unload.
6. For safety: support `Branch` of the skill grain state or the relevant journals for "what-if this new policy".
7. Add test: modify example playbook → re-crystallize → new pack version → embody → old behavior + new behavior both verifiable in separate journals or branches.
8. Verification: journals show versioned `NeuroPackInstalled` + emissions carrying version info (extend `PackEmission` minimally if needed, or put in metadata synapse).

### Phase 6: Invocation Surface & Agent Usability (bottleneck 5, after core works)

1. MCP tools (internal):
   - `list_company_skills`
   - `describe_skill(skillName)` — returns spec + recent journal examples.
   - `invoke_skill(skillName, triggerSynapseType, payloadJson)` — fires to the generated skill grain, returns the emitted outcomes + correlation id.
2. Document the exact synapse contracts the skill understands (so agents outside can construct correct triggers).
3. Optional thin router: a stable `ISkillRouter` grain that maps logical process name to current embodied generated grain.
4. Verify: external (or test) MCP client calls produce correct `PackEmission` chain with full causation.

Do this *after* Phase 4 is solid.

## Verification Discipline (apply after every change)

- Targeted `dotnet test` with precise `--filter` (never blanket "all" unless final gate).
- For any Aspire resource change: `aspire doctor` (MCP) or CLI.
- Embodiment proof always requires observable `PackEmission` with non-fallback output in journal.
- For crystallization/synthesis: human-inspectable artifacts (spec JSON or generated source) asserted in tests.
- Before claiming "core done": run the full ingest→crystallize→synth→embody→execute chain from a clean checkout using only checked-in sources + one command/script.

## Out of Scope (explicitly deleted — revisit only after above is production-usable for one process)

- Live connectors (Slack, Gmail, Jira, DB polling).
- PDF ingestion.
- Multi-company isolation / tenanting.
- Payments, licensing, public marketplace.
- WASM sandbox.
- General UI surfaces / client work.
- Broad self-improvement or meta-optimization loops.
- "Every process" — one (refund) + one follow-up (incident or pricing exception) is enough to prove the machine.
- Optimizing the LLM prompts for unrelated tasks.

## Success Definition (core complete)

A checked-in playbook + transcript for "RefundHandling" can be ingested, crystallized, turned into a real `IPackBehavior` C# pack, installed, embodied via `PackAlcEmbodier` into `GeneratedNeuron`, triggered by a `RefundRequested` synapse (or equivalent), and the resulting journal (with correct `SynapseId`/`CausationId` chain and `PackEmission`) constitutes a verifiable living execution map of that company process. Re-ingesting an updated source produces a new safe version with the same properties.

All prior phases green with minimal, traceable code.

## Order of Work Reminder

Delete and question first. Synthesize the skill generator only after ingestion + crystallization are usable. Accelerate the loop only once the manual path is understandable and repeatable. Automate the closed loop last.

This plan has no filler. Execute the phases in order. Every action exists to close one of the five bottlenecks with the existing substrate.

Next concrete step after reading this: Phase 0 + Phase 1 in a branch. Prove baseline + first ingest of the refund sources.