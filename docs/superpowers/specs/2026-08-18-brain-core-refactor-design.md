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

### C1 outcome divergences from the plan

Actual deletions in C1 (Task 6 complete; Tasks 1–5 earlier):
- **Registry wave survives until C2** (executive ruling): `IRegistry` and supporting grains remain; the full wave deletion deferred to C2 after the Brain absorbs capability plumbing.
- **Workspace** died in Task 5 with the auth slim-down (commit 60c15f67), not Task 1 — one coherent change with the cookie dev-auth replacement.
- **The /authorizations/events SSE surface** (MapAuthorizationStreams + AuthorizationEvent) died in Task 4 with the MCP authorization rail — the rail was its only producer. The Flutter shell's subscription was de-fanged in Task 5 (debugPrint-only onError).
- **Webhook died**: zero consumers in the codebase; removed as dead code.

### C2 outcome

- **The Brain is `Entity<BrainState>`** (T3, absorbed further in T5): one grain per owner, key `{owner}/brain`; register/resolve/contexts/route/learn. Route matches (source, alias), single-target — a second wire on the same pair is refused, not silently unrouted. Connect refuses self-wires, cycles (iterative DFS, closed-path message in the refusal), and duplicate (source, alias) pairs; both endpoints must belong to the connecting owner. Caps: nodes 256 (LRU eviction, purges the evicted node from every context's Members and Tallies too), contexts 32 (LRU, the active context is never evicted), connections 128 (loud refusal, no eviction — wires are never silently dropped).
- **Pipeline deleted in T4**: `NeuronOutbox`/`NeuronMessagePipeline`/`NeuronTurnCoordinator`/`NeuronDeliveryMemory` and the broadcast catalog-route-topology machinery are gone. A send is a journal-staged, direct awaited grain call; replies ride an unawaited call back to the caller (deadlock avoidance between two serialized grains — a lost reply is telemetry, not a retry). Fan-out is Orleans BroadcastChannel (provider `db.broadcast`, namespace `db.activation`, channel key is the subscriber's own grain key, so implicit subscribers activate as regular owner-bound neurons). At-least-once outbox delivery semantics are retired by ruling — accepted pre-production, no compensating retry machinery.
- **Registry + graph absorbed by the Brain in C2 (T5), not later** (supersedes the plan's original C2/C3 split): `DigitalBrainNeuron` and `SynapseGraphNeuron` are deleted; the activation publish that lived on `DigitalBrainNeuron` moved verbatim into `SessionNeuron.Activate`; `SurfaceBoot` is the broadcast receiver that opens the owner's desk surface on session activation.
- **UI target shape (T6)**: `UIRenderer` is the one neuron that writes UI entities, grants-checked on the write path; `IChart` and `ISurface` are pure entities (`IEntity<TState>`/`IPersistentState`, leaf-constructor facet redeclaration required, per the Task 2 pattern). `ChartNeuron`, `Button`, and `Diagram` are deleted. PRECONDITION recorded and still open: a `ButtonClicked` handler (or an explicit boundary refusal) must exist before anything offers buttons again — clicks are currently journaled no-ops (the old route-refusal died with the Button neuron).
- **Introspection module deleted, MCP introspection tools rewritten over the facade (T7)**: `list_active_neurons` semantics shifted from "every activation the Introspection audit saw" to "the owner's brain-registered nodes (live or cold)"; `MapBrainTopology` rewritten onto the Brain registry plus inline Orleans grain statistics. The `Sdk` project is retired. Payload protection (the `IDurablePayloadProtector` trio, `DigitalBrain__Security__StateProtectionKey`, and its whole AppHost parameter chain) is deleted entirely in T8 — registered-but-never-resolved once Introspection's removal orphaned its only caller path.
- **Fast-follows parked by ruling** (deliberately deferred, not regressions): Connect-time alias-acceptance validation (a typo target succeeds at Connect, fails silently at emit as `Unrouted`); wire provenance; per-principal graph partitioning (A18) is gone with no replacement; UI surface writes are ungated durably (parity with the old ungated Surface neuron — `ChartPoint`'s write path is grants-checked, `OpenSurface`'s is not).
- **LOC**: 16,589 src across 325 files (−19.2% vs post-C1 20,527; −48.4% vs the original 32,127); tests 1,218 across 16 files. T8's sweep landed 16,560 src/325 files; T9's riders and quality pass added ~29 src lines and 2 test files (no new src files).
- **Tree accuracy notes (T9)**: path drift confirmed against the working tree — `DigitalBrainModuleBuilder`/`DigitalBrainModuleProjection` live under `Aspire.Hosting/Brain/`; `ModelPayloadSerialization` lives under `Core/Hosting/`; `IModule`, `GrainOwnership`, and `GrainCallerContext` sit at the `Core` project root; `CapabilityHit` and `SynapseAlias` are `Core` types, not `Abstractions` as their tree annotations imply; `ClientDigitalBrainReference` is a naming-order mismatch against the tree's `DigitalBrainClientReference`, not a missing file. Itemized tree sections (as opposed to bare-folder sections) undercounted their folders' real, coherent contents by 2–10x — a documentation gap, not a code gap; future tree edits should whitelist folders instead of itemizing files one by one. `BrainSteps.cs` (Testing.Bdd), named in the p-sdk tree, never existed in git history — it is C4 work (the paused BDD corpus feature), not a C2 omission; marked planned, not missing.

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
