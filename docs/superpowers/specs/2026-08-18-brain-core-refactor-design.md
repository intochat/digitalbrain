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

### C2 final whole-branch review — fixes landed and debt inventory

The closing review (d1b89598..c48424cc) verified every frozen wire byte-compatible across the whole range and found two composition seams no single task created; both were fixed in the phase's final commit (a241a220):

- **Infra-wire refusal**: `BrainWireRules` (Abstractions) denylists call-graph interior neurons (`sessionneuron`, `surface-boot`, `chat-turn-worker`, `grants`) as wire endpoints — the table-walk cycle check cannot see compiled-in call edges, and a wire into the activation chain (e.g. uirenderer → `ui.surface-opened` → surface-boot) deadlocked activation for the 5-minute response timeout. `SurfaceBoot` no longer registers with the Brain (plumbing). Pinned.
- **Silo-side entity registration**: `UIRenderer` registers the chart/surface it writes in the owner's Brain (fire-and-forget, the generalized `RegisterInOwnersBrainAsync(BrainReference)` overload) — previously registration was facade-only, so a corpus-filled chart never resolved. Pinned: a renderer-written chart resolves. §2's "auto-registered" claim now holds for both write paths.

Parked with names (accepted, deliberate — the complete debt inventory as of C2 close):

- The streams/pubsub fabric is provisioned but has zero consumers (queues, tables, keyed clients, sim MemoryStreams, and `RequireStorage`'s streams requirement) — its deletion is C3's mandate (§4); the C3 plan must include the `RequireStorage` signature change and E2E fixture edits.
- The Brain is a single serialized hot grain: every register/resolve-hit/route-hit is a full-state write, and `/graph/events` adds a 1 Hz read poll per SSE subscriber. Invisible under the sim's memory storage; budget for it when the `Default` blob provider goes live in C3.
- `UIRenderer.HandleAsync(ControlActivated)` is an inert no-op (wire-frozen record, nothing fires it) — same family as the ButtonClicked precondition; needs an honest comment or a refusal.
- `SystemTools.ResolveTarget`'s entity redirect adopts the fired contract's host for any chart:/surface:-shaped target (no misroute possible, but the old teaching refusal is gone for that shape), and `RendererEntityGrainTypes` hardcodes UI knowledge in the AI module.
- `BrainEntity` uses bare `DateTimeOffset.UtcNow` at 7 sites; `Entity<TState>` has no TimeProvider seam — recency/tally learning is untestable under simulated time. Fix before C4's learning tests.
- Every unwired lifecycle fact stages an `Unrouted` counter-entry with its own durable write (4 per chat turn); consider `[JournalProjection]` on lifecycle facts or a fact/request distinction.
- Kernel `Dockerfile`'s operator env inventory omits `ConnectionStrings__grainstate` (required since the Default provider landed).
- The `brain:x` type-mismatch refusal message is precise only while the Brain grain is live (cold-brain yields the generic refusal — both refuse; no misroute).
- The untargeted `ui.open-surface` → desk default is pinned at manifest/reflection level only; the runtime wiring through `FireCoreAsync`/`ResolveTarget` has no integration test.

Test gaps to open the C3 plan with: (1) state durability across deactivate/reactivate on the real `Default` provider (nothing pins recovery today); (2) end-to-end wire delivery (`brain_connect` → emit → target's Incoming journal); (3) `/brain/topology` + `/graph/events` smoke over real HTTP (shell-consumed, rewritten twice in C2, zero coverage).

## 4. Fabric (C3 — srcv2 shape)

First-class `DigitalBrainResource : Resource` registered in the model. Fabric: one Azure storage emulator → `clustering` + `reminders` tables, `grainstate` + `journal` blobs; composed via `AddOrleans(name).WithClustering(...).WithReminders(...).WithGrainStorage("Default", grainState)` natives. **No streams, no pubsub** (landed in C3 — deleted outright, not merely unconsumed; see the C3 outcome below). Runtime/client glue collapses to the srcv2 thin shape (keyed clients + `UseOrleans`/`UseOrleansClient` + blob journal storage + configure hooks). Consumer surface: `WithReference(brain)` / `WithReference(brain.AsClient())`, TripRadar-style.

### C3 outcome

- **Resource + parenting landed exactly as designed**: `DigitalBrainResource : Resource` (`src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainResource.cs`) registered via `AddResource(...).ExcludeFromManifest().WithInitialState(...)` — `ResourceType` "DigitalBrain", `KnownResourceStates.Running`, `CustomResourceKnownProperties.Source` "DigitalBrain fabric". Storage, Ollama, Qdrant, and the Flutter shell executable all carry `.WithParentRelationship(brain.Resource)` (four call sites: `DigitalBrainHostingExtensions.AddDigitalBrain`, `AIHostingExtensions.EnsureOllama`, `MemoryHostingExtensions.Enable`, `ShellHostingExtensions.EnsureFlutterHost`) — visual-only, per the decompiled-Aspire verification in Task 2 (hardcoded "Parent" `ResourceRelationshipAnnotation`, no lifecycle coupling; the five-resource health-gate list — storage, clustering, reminders, durableStateStore, grainState — is unchanged). `ClientDigitalBrainReference` renamed `DigitalBrainClientReference` (git mv, similarity 50%, closes the C2 tree-accuracy note). New Aspire pin `BrainResourceExistsAndParentsTheFabric`.
- **Streams/pubsub fabric deleted, zero references remain**: `DigitalBrainNames` lost `Streams`, `PubSub`, `StreamProvider`, `PubSubStore`; `AddDigitalBrain` lost `storage.AddQueues`/`storage.AddTables(PubSub)` and the Orleans `WithStreaming`/`WithGrainStorage(PubSubStore, ...)` chain calls; runtime glue lost the keyed queue/pubsub clients and the `HashRingStreamQueueMapperOptions`/`AzureQueueOptions` option blocks; the sim host (`BrainSimulation.cs`) lost `AddMemoryStreams`/`AddMemoryGrainStorage(PubSubStore)`. Packages `Aspire.Azure.Storage.Queues` and `Microsoft.Orleans.Streaming.AzureStorage` removed from `DigitalBrain.Aspire.csproj` and `Directory.Packages.props` (verified: zero remaining references in either file). `RequireStorage(IConfiguration)` narrowed from `(string Clustering, string Streams)` to `string` — returns the clustering connection string only; its sole caller (`BrainAppHostFixture.ConnectScriptHostAsync`) uses it as a bare fail-fast validation call, the return value unused.
- **Three C2-named test gaps closed (Task 1, all first-run PASS)**: `WireDeliveryTests.AConnectedWireDeliversTheEmissionToTheTargetsIncomingJournal` (`tests/DigitalBrain.Simulation.Tests`) pins `brain_connect → emit → target's Incoming journal`; `FabricSurfaceTests.RendererWrittenChartStateSurvivesActivationCollection` (`tests/DigitalBrain.E2E.Tests`) pins state durability across `ForceActivationCollection` on the real `Default` blob provider; `FabricSurfaceTests.BrainTopologyAndGraphEventsServeTheShellWire` smokes `/brain/topology` + `/graph/events` over real HTTP with the auth cookie. New SDK surface: `BrainAppHostFixture.GrainsAsync()` (`src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs:115`), reusing the fixture's already-built `IGrainFactory` rather than constructing a second host.
- **Runtime/client glue confirmed at the srcv2 thin shape**: the silo (`DigitalBrainRuntimeHostingExtensions.AddDigitalBrain`) wires keyed clustering/reminders table clients + a keyed grainstate blob client + `UseOrleans` (durable state, module registration, dashboard); the client (`DigitalBrainClientHostingExtensions.AddDigitalBrainClient`) wires a keyed clustering table client + `UseOrleansClient`. `WithReference(brain)` keeps three references — Orleans, `DurableStateStore` ("journal"), `GrainState` ("grainstate") — a deliberate divergence from srcv2's two-reference shape (the C2 outcome section records the canonical pattern: the silo's keyed grainstate blob client needs the connection string). `WithReference(brain.AsClient())` keeps `Orleans.AsClient()` only.
- **Conformance suites**: `TopologyConformanceTests.FabricResourceExists` and `NamesConformanceTests.KernelRenderedEnvironmentContainsFabricConnectionStringKey` each lost their `Streams`/`PubSub` `InlineData` rows (4 rows total) — the only intentional pin change. Aspire suite: 17 baseline (C2 close) + `BrainResourceExistsAndParentsTheFabric` (Task 2) = 18, − 4 deleted rows (Task 3) = **14/14**, all passing.
- **Kernel Dockerfile env inventory fixed** (the parked C2 debt item): added the missing `ConnectionStrings__grainstate` line; corrected the stale `ConnectionStrings__brain-clustering`/`ConnectionStrings__brain-reminders` names to `ConnectionStrings__clustering`/`ConnectionStrings__reminders` (matching what the runtime resolves and what `NamesConformanceTests` pins); section label widened to "Orleans clustering, reminders, grain state, journal".
- **LOC** (`find {src,tests} -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" -print0 | xargs -0 cat | wc -l`, measured at HEAD/`2917270c`): src **16,623 across 327 files**; tests **1,432 across 19 files**. Against the C2-close-plus-fix baseline (measured the same way at `de55a4a9`, the commit immediately preceding Task 1): src was 16,637/326 files (C2's recorded 16,589/325 + the `a241a220` composition-seam fix's +48/+1), tests were 1,281/16. Net C3 change: src **−14 lines, +1 file** (new `DigitalBrainResource.cs`; the `DigitalBrainClientReference` rename is file-count-neutral); tests **+151 lines, +3 files** (`TestNeurons.cs`, `WireDeliveryTests.cs`, `FabricSurfaceTests.cs`, all Task 1).
- **Deviations discovered across Tasks 1-3**: none functional. Task 1 needed two additional `using` directives in `TestNeurons.cs` (a global-using scoping mismatch, not a design gap) and a non-`async` `GrainsAsync()` body (the brief's suggested lazy-accessor pattern doesn't exist in this fixture version; reused the eagerly-built `_grains` field instead — same observable contract). Task 3's Dockerfile fix corrected two stale connection-string names beyond the rider's literal "add grainstate" scope, to avoid shipping a self-contradictory inventory next to the freshly-corrected line.
- **Known nit, deferred**: `FabricSurfaceTests.BrainTopologyAndGraphEventsServeTheShellWire`'s SSE `HttpResponseMessage` (`events`, line 57) is never disposed — hygiene only, no leak under test-process lifetime; carried forward rather than fixed in C3.

### C3 final whole-branch review — fix landed and C4 carry-forward

The closing review (de55a4a9..2736a565) verified the fabric trace complete and minimal (every keyed client the silo/client demands maps to exactly one `WithReference` projection), the resource genuinely passive (nothing waits on it; the E2E isolation loop skips it), the streams/pubsub deletion zero-remnant repo-wide, and the three T1 pins unweakened by the rewrite. One Important find, fixed in the phase's final commit (b774fa91): `docker-compose.yml` omitted `ConnectionStrings__grainstate`, so the standalone compose deploy would die at first entity activation while the freshly corrected Dockerfile inventory beside it claimed otherwise — the warm E2E fixture could never see it. The same commit added the missing `InlineData(DigitalBrainNames.GrainState)` conformance row (Aspire 15/15).

Parked by ruling at C3 close:
- The wire-delivery pin exercises `IBrain.Connect` grain-directly; the MCP `brain_connect` TOOL surface is still untested — C4's "check chart" demo should be the first thing to cross it.
- The durability pin has a theoretical false-PASS mode (a busy activation surviving `ForceActivationCollection` would serve in-memory state); accepted as the standard Orleans pattern — C4's learning tests must add an activation-identity assertion if they depend on genuine reload semantics.
- The C2 hot-grain budget item's trigger has fired: the `Default` blob provider is live and no measurement was taken. C4 inherits it explicitly.

C4 plan carry-forward (open with these): (1) the `BrainEntity` TimeProvider seam — spec-mandated before learning tests, deliberately untouched in C3; (2) `Testing.Bdd`/`BrainSteps.cs` does not exist yet; (3) the `ButtonClicked`/`ControlActivated` preconditions and the untested untargeted `ui.open-surface` runtime wiring (both touch C4's UI evidence); (4) the hot-grain write-budget measurement; (5) the untested MCP `brain_connect` surface; (6) the deferred SSE-disposal nit if convenient.

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
