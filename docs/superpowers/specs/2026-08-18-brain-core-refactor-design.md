# DigitalBrain: Brain Core Refactor (CoreV3)

**Date:** 2026-08-18 · **Branch:** `finalv2` · **Status:** Approved design
**Supersedes:** the architecture sections (§5, parts of §2/§4) of `2026-08-18-digitalbrain-aspire-testing-sdk-design.md`. The testing-SDK design (§6), test-mode contract, and BDD/mock-LLM design (§7) remain in force; the phase plan (§11) is replaced by the C-phases below.

## 1. Intent

The initial solution is a minimal set of **extremely well-designed, testable abstractions** with a couple of reference examples covered by tests at all three tiers. The brain itself becomes the thing that *knows and learns*: a living registry + graph + router with working contexts. Roughly 60% of the current 32,127 LOC is deleted as a consequence of the concept census — the deletion is an outcome, not a goal.

## 2. The five abstractions

1. **Neuron** — a journaled actor: receives/sends typed synapses, logs its traffic in two bounded journals (Incoming/Outgoing, 512/512KB, tallies, reset-snapshot — semantics pinned by the Tier 2 suite).
2. **Synapse** — a typed message between neurons.
3. **Entity** — plain readable/writable state, no messaging, never a graph endpoint. Persists via Orleans-native **`Default` grain storage (blobs)**; Orleans.Journaling remains ONLY for neuron traffic journals (dual model, srcv2 shape).
4. **Brain** — the owner's living registry + graph + router, one grain, one state (`Entity<BrainState>`):
   - **Knows**: every active neuron and entity, auto-registered on first activation (no manual registry calls).
   - **Contexts**: `BrainState.Contexts` — attention frames: `BrainContext(Name, Members: BrainReference[], LastUsed)` + a server-side `ActiveContext` pointer (one brain, one attention) with per-call override. A context is NOT an auth boundary, NOT tenancy, NOT a partition, and NOT a chat (it spans chats, MCP calls, and timers; chat turns reference it). Auto-created `"default"`, explicit `UseContext` switching, capped count. Snapshot records only — no live client-side context objects.
   - **Resolves**: `"chart"` → `chartentity:owner/demo`, scoped to the active context (name + type + per-context recency).
   - **Routes**: graph connections first, capability search (hybrid `CapabilityIndex`) second — absorbing `SynapseGraphNeuron` and the registry machinery.
   - **Learns**: per-context usage tallies bias resolution and routing. Plain counters; inference may later *bias*, never silently switch, the active context.
5. **Memory** — what the brain remembers long-term: facts + vectors (Qdrant). Absorbs the Corpus concept (story facts become memory facts; the name Corpus dies).

**Facade additions** (`IDigitalBrain`): `ActiveContextAsync()`, `ContextsAsync()`, `UseContextAsync(name)`, `ResolveAsync(hint)` → `BrainReference?`. Existing members unchanged.

**Reference examples** (kept polished + test-covered at all tiers): `ChartEntity` (the entity done right) and `Chat` (the neuron done right — slimmed once Execution dies and a turn becomes an awaited grain call).

## 3. Concept census — dies / rides along / stays

**Dies in C1** (git history is the archive; each deletion commit carries its minimal replacement so all suites stay green per commit):
- **Execution module** (3.5k LOC) → chat turn = direct awaited call to the turn worker + Orleans timers for liveness.
- **Library / Registry / Behavior / Workspace / Repository** waves + their MCP tools (Repository is already dead code).
- **CorpusNeuron + contracts** → Memory fact append/read (call sites: Chat, ScheduleNeuron, MCP `read_corpus` → memory read).
- **MCP authorization rail** (~3k) + **Google/Salesforce stub modules** + `OAuthProviderHosting` + `MapOAuthCallback` — return when the modules are real.
- **Hand-rolled auth** (1.2k) → minimal cookie dev-auth (~150 LOC) serving exactly the shell's three endpoints (`/auth/login`, `/auth/me`, `/auth/bootstrap`).
- **Scripting project + resource** (probes are superseded by the test SDK).

**Dies in C2:** `NeuronOutbox`/`NeuronMessagePipeline`/`NeuronTurnCoordinator`/`NeuronDeliveryMemory`/broadcast catalog-route-topology (→ plain grain calls + Orleans **BroadcastChannel**); `SynapseGraphNeuron` + `DigitalBrainNeuron` (→ the Brain); registry-flavored capability plumbing the Brain absorbs.

**Rides along untouched** (zero redesign investment until the core is done; patched only if a deletion breaks compilation): Voice (real, shell-consumed), Introspection module + tools, Team orchestration, Surface/Button/Diagram.

**Stays and is load-bearing:** traffic journals, owner wall + entry-point filters, Grants, `CapabilityIndex` (hybrid + fallback), Time module (re-based on native reminders in C2 if cheap), Memory module, the AI assistant + tools, the whole testing SDK.

## 4. Fabric (C3 — srcv2 shape)

First-class `DigitalBrainResource : Resource` registered in the model. Fabric: one Azure storage emulator → `clustering` + `reminders` tables, `grainstate` + `journal` blobs; composed via `AddOrleans(name).WithClustering(...).WithReminders(...).WithGrainStorage("Default", grainState)` natives. **No streams, no pubsub** (nothing consumes them after C2). Runtime/client glue collapses to the srcv2 thin shape (keyed clients + `UseOrleans`/`UseOrleansClient` + blob journal storage + configure hooks). Consumer surface: `WithReference(brain)` / `WithReference(brain.AsClient())`, TripRadar-style.

## 5. Ordering and gates

| Phase | Content | Gate |
|---|---|---|
| C1 | Concept-census deletions, each commit paired with its inline replacement | build `-warnaserror` + all three suites green per task |
| C2 | The Brain (`Entity<BrainState>`: registry, contexts, resolve, route, learn) + pipeline removal + facade additions | suites green; new Tier 2 Brain tests (resolve/context/route) |
| C3 | Fabric rewrite to the srcv2 resource shape; Entity → `Default` grain storage | suites green; Tier 1 conformance updated for the new fabric |
| C4 | Resume the paused MVP e2e (BDD corpus feature, UI evidence) on the slim core; "check chart" resolution added to the demo scenario | the §9 flow (amended: + resolve) green at all tiers |

## 6. Risks

- **Cleanup-before-fabric ordering** (user's call): mitigated by the paired-replacement rule — no commit leaves the suites red.
- **Chat.cs rewrite** (Execution removal) is the riskiest single change; the Tier 2 chart-flow and Tier 3 e2e suites are its net, and the turn pipeline's states (Pending/Running/Completed lifecycle synapses) must keep their journal footprint (the SSE edge and Flutter depend on `TurnLifecycle`/`Responded`).
- **Auth slim-down** must keep the Flutter shell's cookie flow working byte-for-byte at its three endpoints.
- **Pinned tests that intentionally change** (e.g. scripting-resource conformance, corpus test from phase-3 T5) are updated in the same task as the change, with the ledger recording each intentional pin change.
