# DigitalBrain — Progress & Archaeology (Cross-Version Analysis)

This document is the living synthesis of every top-level folder under E:\Projects. It was produced by enumerating directory trees (list_dir), reading every key manifest (README, CLAUDE.md, ORIGINAL_REQUEST.md, PROJECT.md, all docs/*.md), targeted literal reads of representative source files and .ino/.brain capsules, and content grep for the invariants (neuron, synapse, IHandle<>, IEmit<>, Simulation, BundleInstalled, InstallBundle, marketplace, Creator, InoLang, etc.).

All work stayed strictly inside E:\Projects (no C:\Users paths). The goal: recover the initial idea of each reboot, the clean/product features that were mined or cut, and feed distilled lessons into final/ (the canonical).

## Canonical Rule (applies to every version, strongest in final)

From v2/00-manifesto.md (the clean-room definition that survived):

> Everything is a Neuron or a Synapse. No exceptions.
> A neuron is an actor (Orleans grain). A synapse is an immutable typed message, broadcast on a shared timeline or sent point-to-point.
> Wiring is declared on interfaces (IHandle<T> / IEmit<T> on Contracts) so the dispatch manifest and graph are scannable without loading implementations.
> A test is a Simulation, and a Simulation is a neuron (fire + expect on the live timeline = the gate for AI-authored + human proofs).
> The marketplace proof is dynamic: after InstallBundle / BundleInstalled the same broadcast reaches N+1 handlers and the new handler reacts — without silo restart.
> IDigitalBrain is itself a neuron; the brain (not the AppHost) owns orchestration.

final/docs/VISION.md, DISTRIBUTION.md, ROADMAP.md, DELETED.md, USER-FLOWS.md and the DistributionDynamicHandlers.feature (17/17 scenarios at time of writing) are the current executable embodiment.

## Per-Folder Analysis

### final/ (canonical clean reboot — all new work here)

Role per root CLAUDE.md and final/docs/*: Aspire 13 + .NET 11 + clean neuron/synapse core + Reqnroll BDD. Thin AppHost. SourceGen dispatch manifest. Per-grain journals via JournalStore + IDurableList. hex1b TUI (TabPanel Ask/Creator/Market + generic SurfaceRenderer). Private contract-only bundles (recent).

**Key source files read literally (Core + selected Kernel/Tests):**
- Domain/Events/Synapse.cs: abstract record + SynapseMetadata (SynapseId, CorrelationId, CausationId, Caller/Receiver NeuronId, RoutingMode, BrainScope, Timestamp). `Stamp(NeuronId firing, Synapse? incoming)` threads corr/caus/receiver/lineage. All concrete synapses (Activated, BundleInstalled, InstallBundle, ReviewProjectRequest, UiSurface, WeatherQuery, SetAlarm, NeuronTelemetry, SynapseIncoming/Outgoing, etc.) derive and carry the same envelope.
- Application/IDigitalBrain.cs (and INeuron.cs sibling): INeuron (IGrainWithStringKey) + SendAsync, ListSubscribersAsync, ListActiveNeuronTypesAsync, GetRecentHistory/GetFullJournal, InstallBundleAsync/Publish/StartWorld/Launch resolvers (Simulation vs AspireHosted vs ConnectExisting), worlds as isolated clusters. IDigitalBrainClient for peer.
- Infrastructure/Orleans/Neuron.cs: Grain + IAsyncObserver<Synapse>. OnActivate: per-grain Incoming/Outgoing journals (JournalStore.GetOrCreate(Self) for isolation — fixes earlier silo-wide shared list interleaving), SubscribeTimelineIfNeeded only if HandledTypes > 0 (via dispatch), EmitActivated. Receive: depth guard (RequestContext), append Incoming, emit SynapseIncoming wrapper (best-effort), Dispatch. Emit/Ask/Reply: StampForRoute + append Outgoing + timeline publish (broadcast) or Deliver + SynapseOutgoing (p2p). GetJournalHistory/Full/Lifecycle helpers.
- Infrastructure/Orleans/SynapseDispatch.cs: Prefers DigitalBrain.SourceGen.DispatchManifest (KnownHandlers / KnownContracts with IsHandle flag for private contract bundles) → pre-resolved invokers or FrozenDictionary. Falls back to reflection over IHandle<> interfaces. ManifestAvailable assert for loud failure on generator regression. HandledTypes cache. Used by Neuron.OnNext and direct.
- Core.Tests/Simulation.cs: TestCluster + ConfigureDigitalBrainDefaults (journals, streams, setup). StartAsync returns cluster/grains/brain. VerifyDurableJournalReplay (every run): send marker, history appears, re-acquire (re-activation), cross-"user2" domain-key authoring/pack/publish/install + reminders + voice transcription demo. "A Simulation is a neuron".
- Core.Tests/DistributionDynamicHandlers.feature (and bindings): THE non-negotiable. 17+ scenarios covering: install grows subs + demo handler reacts to BundleInstalled; publish+install; lifecycle (Activated/Incoming/Outgoing) first-class on timeline; weather-watcher create-ino + https + cross install + react; kernel-tasks alarm widget; awesome SE bundle (src-located, typed); pack from journals + publish + account-b install (N+1, no restart); marketplace surface render + button tap roundtrip (OnTap → InstallFromMarketplace); private contract-only (pack/publish with decls only, no .ino/impl leak, account-b install records ContractBundles not full, ListSubscribers grows via contract contrib + sim double handler from test asm KnownContracts, dispatch works, mixed + re-activate). All via real timeline stream + SynapseDispatch (manifest or fallback) + per-grain journals.
- start.cs: file-based web+orleans (per-world/pid cluster), ollama gemma, ConfigureDigitalBrainDefaults, SurfaceStream gRPC (flutter leg), client cluster, DigitalBrainLauncher.EnsureDomainExperiencesAsync (seeds awesome etc), TUI/Flutter best-effort.
- Other (from trees/greps): DigitalBrainGrain.cs (InstallBundle logic + subscriber math + ContractBundles for private), PackagerNeuron.cs (journal → .brain capsule SHA + manifest with observedSynapses), MarketplaceNeuron.cs (listings, verify hash, InstallFromPeer via MarketplacePeer cluster client), CreatorNeuron, LlmAgentNeuron (tools incl review_project on kernel-local path), KernelTaskSupervisor (reminders + surfaces), UiNeuron/SurfaceStreamService, Widgets.cs (UiWidget union: Button/Text/Card/Column/Row/Markdown — concrete arrays for Orleans codec; WidgetTree.Render), SourceGen generator (scans IHandle/IEmit, emits DispatchManifest + pre-resolve hooks), Sdk (IAspire restart surface, MarketplacePeer, FileSystem neuron, Flutter shims), AppHost (thin, env DIGITALBRAIN_*, reflection-load KernelHost), Awesome/SoftwareEngineeringTeam (ReviewRequest + ReviewProjectRequest → ReviewResult + review: Markdown surface), pa-files/*.brain (manifest + experience.ino or pre-shipped activation).

**Current status (from ROADMAP + docs read):** Phase 0 (distribution vertical + TUI redesign to tabs + Markdown + private contracts) DONE and green. 18/19 suite (1 known NeuronE2ETest skip for high-sev — ino E2E pending full Playwright wiring). run-ci.ps1 is the gate. Phase 1 hardening pending (real per-grain durable journals beyond stub, Redis for marketplace state, ListSubscribers observe not compute, advertised IP LAN, drop dynamic activation).

**Mined from history:** v2 capsule layout + Simulation=neuron substrate + creator loop + .ino lowering + interface wiring + dispatch; ino/ E2E/BDD patterns + multi-domain/world isolation; IAW hosting richness (now thin + brain-owned); v1 IDigitalBrain orchestrates resources (now IAspire + StartWorld); product ambition (ino name, experiences as shareable lived behavior, surfaces as first-class synapses).

**Cuts (see DELETED.md):** full rich Flutter per-neuron UI + living canvas + constellation (14k+ lines), gRPC as primary client transport (Orleans timeline is the one; gRPC kept only for flutter surfaces), usage.json in capsules, GetListingAsync, early global peers state, llm-chat junk surface emission, heavy duplicated Verify in sim, client kernel-tasks install, mcps/ dup inside final/, flutter-client AppHost resource + full client project (fast path is dotnet run start.cs + hex1b), Row usage in most surfaces (kept in union for journal replay safety), etc.

### v2/ (clean-room prototype — strongest philosophical ancestor)

No top README (PROJECT.md + v2-clean-room-prototype.md + docs/00-05). Initial idea (00-manifesto.md): "Start from scratch. Bring only the required minimum. Everything is a neuron or a synapse." Five non-negotiables: two primitives, one verb (Fire with routing metadata), wiring on Contracts interfaces (IHandle/IEmit scannable), Simulation is a neuron (Fire/Expect on live silo = test = AI gate), two notations one shape (.ino lowers to C# capsule).

**Structure (list + src/ + bundle projects read via trees/greps):** DigitalBrain.V2.Core (Synapse with Metadata + Stamp, Neuron base with OnNext/Deliver/Emit/Ask/Reply + depth, IDigitalBrain, RoutingMode), DigitalBrain.V2.Catalog (CatalogNeuron + Scanner for IHandle/IEmit), DigitalBrain.V2.Creator (Architect/Implementer/Gate/Llm + LoopResults; .ino authoring gated by Simulation), DigitalBrain.V2.Ino (Parser/Ast/Compiler/Transpiler — neuron/using/handles/broadcasts/on:/scenario/ui: sections), DigitalBrain.V2.Testing (Simulation base + Substrate), capsules/ (Ping: Contracts + Impl + Simulations with .ino; Greeter with Bystander/Room for broadcast vs p2p proof), many "bundle" projects (DigitalBrain.Awesome, Ai.Llm, Ai.Search, Auth.Google, Data.Postgres, Windows.FileSystem, Microsoft.Aspire/Roslyn, XAI.Grok, Marketplace, Ino, SDK) each with Bundle.cs + bundle.json + Testing/*.ino.

**Clean features/product:** Creator loop (architect → implementer → LLM → gate transpiles+compiles+sim gate → NeuronActivated); .ino as self-contained (contracts + behavior + telemetry + state + ui + scenario); capsules co-located (Contracts+Sims+impl); Simulation as the only test substrate; bundles first-class (project + manifest); catalog from interfaces only; marketplace (rich in DigitalBrain.Marketplace with Stripe/licensing later); interpreted .ino hot-reload intent.

**What fed final / cut:** The entire Core Law + Simulation substrate + dispatch manifest idea + creator + .ino lowering + capsule shape + "N+1 on install" directly became final's Core + Kernel experiences + Reqnroll Distribution feature + pa-files capsules + SourceGen. Cut from day one (per manifesto): heavy marketplace/stripe early, spatial UI, LLM swarm, durable tasks, multi-cluster federation, FakeDigitalBrain, complex 10-field SimulationSpec.

**Verification notes (v2-clean-room-prototype.md):** 5/5 simulations green; slices A-D (Greeter p2p/broadcast proof, Catalog, .ino+Roslyn+dynamic, Creator loop with real model via synapses).

### v1/ (heavy InoLang + product ambition + brain-controls-Aspire)

README + CLAUDE + ORIGINAL_REQUEST.md + docs/v5plan/* + v6plan + architectural_blueprint + DIGITALBRAIN_RESEARCH.md + implementation_plan. Initial idea (multiple requests): "The operating system of new generation. Talk to it; it writes the code, gates the tests, activates the behavior. Multiple private brains per machine." Heavy Flutter UI (Constellation of brains, Brain Scene, visual Neuron Constructor with GestureDetector+CustomPainter nodes/lines, Ino Code Editor syntax sync, living canvas unification cutting 14k legacy lines), InoLang (full inolang/ AST/parser/compiler to Roslyn scripts, DynamicNeuronGrain, hot-reload on file/UI save), brain controls Aspire (IDigitalBrain restarts live resources, AspireBootNeuronHost, kernel/AppHost split), rich kernel (Creator/Cortex/Navigator/Introspector/Visualization/OS/Runtime/User), massive SDK (Google OAuth/Gmail/Calendar, Postgres/Sqlite, Stripe, Telegram, XAI, Windows, Ai, Aspire, Canvas), digitalbrain.cs single-file launch, samples .ino, UI/flutter (231 files Dart + rive + assets).

**Clean features:** Visual + code dual authoring of neurons, live hot-reload topology, brain as orchestrator of real Aspire worlds/resources (the "IDigitalBrain restarts live" idea), rich connectors as SDK, multi-brain isolation (BrainId prefix on state/OAuth/storage), RFW surfaces declared in .ino, spec-first (every neuron .feature + Steps + impl triplet, later cut toward .ino scenarios), E2E with Playwright/gRPC on RFW payloads.

**What fed final / cut (pain → lessons):** Brain-as-orchestrator survives (now IAspire + StartWorld + launcher + kernel-owned decisions; AppHost deliberately thin). Ino name + assistant product vision survives. Rich per-neuron UI + living canvas + constellation + blank-screen fights + 14k line cuts → final pivoted to hex1b TUI + generic WidgetTree/SurfaceRenderer + minimal union (only Markdown added). Heavy InoLang dynamic + Roslyn everywhere → final keeps lightweight .ino for authored experiences (Packager + Creator) but core neurons are C#; SourceGen for static manifest. Complex test harnesses (4 overlapping) → final single Simulation=neuron + Reqnroll over TestCluster for the one proof that matters. Original requests (UI fix, Ino editor, hot-reload, living canvas s1) record the ambition and the ruthless simplification that produced the clean reboot.

**Other artifacts:** inolang/ with tests, kernel/ with 395 cs files (heavy), sdk/ 340 cs, UI/flutter, examples .ino, docs with v5plan cut list (70% reduction: one file per behavior, no Signal subtype, no global MapCatalog, UI data in .ino, domains as GitHub repos), ORIGINAL_REQUESTs focused on blank UI + visual constructor + unification.

### v3/

Small. DigitalBrain.V3.slnx + docs/ (copies of v2 00-05 manifestos) + src/ (exact v2 capsule layout: capsules/Ping+Greeter with Contracts/Simulations/impl + .ino, Catalog, Core, Creator, Ino, Testing). "Capsule layout (Contracts + Simulations + impl co-located), Ino transpiler sims." Transitional; v2 ideas hardened into the folder structure that final inherited (pa-files + Awesome as pre-shipped, tests co-located with contracts).

### v4/

Fresh Aspire template reboot. DigitalBrainTech.slnx + AppHost + Web (Blazor Counter/Weather + bootstrap) + ApiService + core/ (DigitalBrain.Abstractions: IDigitalBrain, bundles IBundle/IBundleInstaller/IBundleSource + BundleInstalled etc, tasks IDurableTask*, secrets, comms), kernel/ (BundleInstaller, Global/LocalDiskBundleSource, HostingExtensions), sdk/ (thin), ino/ sub, samples/ (Ping.ino + consumer tests + bundle.json), localpkgs nupkgs, nuget.config. "Fresh Aspire template scaffold (AppHost + web + api + redis), thin abstractions." Bridge from heavy v1 to clean v2-shaped final (bundle/install concepts, Aspire hosting, but still more traditional layering before pure neuron/synapse Core).

### IAW/

Rich Aspire hosting experiments. IAW.slnx + CLAUDE (detailed: prefer live iaw MCP `assistant_chat` / `agent_send_message` / `agent_get_events` over mocks for agent behavior; build/test commands; Agent base class split (Core/Events/Lifecycle/State/Streams/Tools/Scheduling/Observers); durable via Orleans Journaling + IDurableList/Dict; constructor attrs [AgentState]/[Llm<T>]; behavioral verification loop). README: "Interactive Agents Web" — team of specialized agents (Project/Code/Reviewer/Memory/Build), observable (Aspire traces, model/token/latency/decision chain), mix providers (cloud + local Ollama), memory, self-improving, Telegram/MCP/Web UI entry. src/: Core/Agents (Agent.cs + partials), Aspire (hosting + client), Agents.CSharp, Agents.Host, Aspire.Client, DevUI, MCP, Telegram, Testing (AgentTest + MockChatClient), Core. docs/ with architecture + superpowers + orleans_scheduling + durable-tasks research. test/ with Core/Integration/E2E.

**Clean features/product:** Observable agent framework (traces + events + tool approval + memory + scheduling as first-class), multi-LLM tiers + voice, Qdrant vector, Orleans dev with Journaling, MCP/Telegram integration, DevUI. "Rich Aspire hosting — LLM tiers, voice, Ollama, Qdrant, Orleans dev."

**What fed final:** LlmAgentNeuron + tools + transcription + memory patterns, Kernel experiences style, Aspire integration (now thin AppHost + ConfigureDigitalBrainDefaults + IAspire surface), behavioral/live verification culture (final uses Reqnroll + start.cs manual + collector/probe for timeline). Cut: the full IAW agent base (final uses plain Neuron + dispatch + journals); heavy hosting moved out of core.

### ino/ (strongest E2E + multi-domain + "ino" product name)

ino.slnx + aspire.config + detailed CLAUDE.md (authoritative per root) + README (product vision: AI-native OS inside every OS; three primitives neurons/synapses/self-improving; synapse = signal + memory(decay 0-100 + sleep consolidation) + thinking (C# code-carrying for Turing); L1/L2/L3 self-improvement; domains as first-class). Huge: domains/ (~3862 files: testing, taxi (Uber MCP), travel (TripRadar) + tripradar sub), clients/ (ino.flutter with Rive persona_orb + BLoC/GoRouter/OTel/gRPC-Web, Telegram host), src/ (Ino.Core (NeuronId/DomainId/Caller/CorrelationId/EventEnvelope/ISynapse + attrs + LlmTier + RfwPayload), Ino.Core.Hosting (Neuron/LlmNeuron bases, FirePort/AmbientFire/CapabilityEnforcer/Discovery, CortexCapability, Journaling extensions, InoOrleansEndpoints, Llm provider factories + BddMock, Registration, TraversalEngine, ML optimizer), Ino.Aspire.Hosting (AddIno/WithDomain/WithLlm/WithVoice, InoBuilder, MarketplaceFeed, NeuronIdJson etc), Ino.AppHost + .Testing, Ino.Kernel (CortexNeuron/Discovery/Kernel/MarketplaceController + wwwroot), Ino.Gateway + .Grpc (IInoGateway, proto, services), Ino.Identity, Ino.Llm.Xai, Ino.ServiceDefaults, Ino.NeuronTesting (NeuronE2ETest + NeuronPage/RfwSnapshot/SynapseFire + BDD steps), Ino.Testing + .E2E (multi-silo fixtures, browser, capture)), test/, iaw/ sub (the substrate), website/, docs/ (156 md: product-vision-final with 14 locked decisions, plans), reviews/ screenshots.

**Clean features/product:** "ino" as the personal assistant product (sits on IAW substrate; AddIno delegates to AddIAW); Neuron<TEvent> (pure code, journal-event) vs LlmNeuron (inherits IAW Agent for chat/tools/history); synapse unifies signal/memory/thinking; self-improving loop (persisted new neuron L1, reasoning-time Roslyn L2, compiled L3); strong E2E (NeuronE2ETest + Playwright over generated UI/RFW/gRPC + multi-silo + browser fixtures); multi-domain silos (kernel/identity/travel/taxi as IDomain markers, separate silos, shared substrate); Cortex routing + Discovery + FirePort ambient; voice (WebSpeech + providers); marketplace feed; Rive persona orb client assets; Telegram mini-app.

**What fed final / cut:** The "ino" name + assistant vision + "experiences spread capability" (final VISION: "ino is a neuron among neurons", experiences as capsules with evidence from journals). E2E/BDD culture + multi-world isolation (final uses domain-key "account-b"/"user2" + StartWorld + peer cluster client). Strong Playwright + generated UI proof (final keeps NeuronE2ETest [Skip] + Distribution feature for high-sev; Flutter client slimmed to surface renderer only, full rich client in clients/ino.flutter is archaeology). Synapse-as-memory-with-decay + code-carrying thinking is richer than final's pure signal + separate journals (final kept simple immutable + durable Incoming/Outgoing per neuron for causal replay). Cut for final clean reboot: the full IAW/ino substrate + heavy domain projects + rich clients in the fast path (dotnet run start.cs + hex1b is the inner loop).

**Per CLAUDE:** "Strongest E2E (NeuronE2ETest + Playwright over generated UI), multi-domain silos, BDD. Has its own detailed CLAUDE.md."

### mcps/

Reusable MCP server tool definitions (json). codegraph/ (callers/callees/explore/files/impact/node/search/status), context7/ (query-docs, resolve-library-id — "ALWAYS use Context7 to look up ALL package/framework APIs before writing ANY code"), dart/ (full flutter/dart tooling: analyze/hot-reload/get_widget_tree/launch/run_tests etc), playwright/ (browser_* full automation), very-good-cli/ (create/packages/test/licenses), microsoft-learn/, headroom/.

**Role:** "Reusable MCP server configs copied into each project." final docs + root CLAUDE mandate their use (Context7 for .NET/Orleans/Aspire/Hex1b etc; no local NuGet cache for API lookups). Copied trees in v2/ino/ etc were sometimes deleted as dup (see DELETED).

## Cross-Version Evolution & Lessons

**Invariant line (v2 manifesto → all later → final):** Two primitives + interface-declared wiring (for provability + manifest without impl load) + Simulation=neuron (test/gate same machine) + dynamic N+1 on install (Core Law marketplace contract, proven only via the Reqnroll substrate in final) + journals/timeline as observable truth + ino as peer + .ino/C# interchangeable shape.

**Product ambition (v1 + ino/ + IAW + final VISION):** Living brain of shareable experiences (lived-with evidence from journals, not just code). Friend on LAN installs your weather-watcher or SE review bundle and it just works on their timeline. <60s from "I use this" → "my friend's brain does too". 0 restarts on install. 100% listings with real usage evidence.

**Hosting & orchestration:** v1 brain controls Aspire (strong). IAW rich tiers (LLM/voice/Qdrant). v4 scaffold. final: deliberately thin AppHost (env-driven, reflection KernelHost); the *brain neuron* owns StartWorld/RestartResource/Flutter/launch decisions via IAspire surface + launcher. Per-world clusters (domain key or ClusterId) for isolation without accounts.

**UI/clients:** v1 heavy Flutter (visual constructor + editor + canvas + constellation, RFW per .ino). ino/ rich flutter (Rive persona) + Telegram. IAW DevUI. v2/v3 minimal. final: hex1b TUI (fast REPL + headless testable via input sequences + screen assertions; TabPanel + generic renderer + Markdown only union extension; surfaces route by id prefix). Flutter kept as optional gRPC surface peer renderer (best-effort or Aspire-driven). gRPC client transport primacy deleted (Orleans timeline is the universal client transport).

**Authoring & marketplace:** v1/v2 InoLang + creator (architect/implementer/gate + LLM) + .ino scenarios. ino/ self-improvement L1-3 + Roslyn in reasoning. final: Packager (journals → capsule with observedSynapses evidence + SHA), MarketplaceNeuron (local + peer via cluster client), Creator (proposals + ActionCreateIno + ActionRunSimulation gate), LlmAgent with tools (incl real kernel-local review_project), awesome-se pre-seed, private contract-only (shape only, no impl, still causes N+1 via KnownContracts). Capsules .brain (manifest + .ino or pre-shipped activation). No executable CIL in v0 (Phase 4 later).

**Testing:** v1 440+ sequential (port contention, no parallel). v2 Simulation base + 5 sims. ino/ NeuronE2ETest + Playwright + multi-silo + BDD steps. IAW live MCP behavioral + AgentTest. final: Reqnroll over real Orleans TestCluster + Simulation substrate; the DistributionDynamicHandlers.feature is the one gate that must stay green (high-sev filter); 1 known E2E skip; run-ci.ps1; collector grain + probe for timeline roundtrips; WidgetTree.Render assertions; ProjectReview.Analyze unit. "Tests are executable specs."

**Cuts that enabled the clean reboot (DELETED.md + session handoffs):** Everything that violated "minimum that still proves the law". Heavy UI, dual transports, fake brains, complex specs, duplicated setup, committed capsules (runtime only), gRPC client primacy, per-neuron Dart, full flutter in fast path, etc. Kept for safety: Row case in UiWidget union (journals may replay it).

**What final deliberately kept vs lost:**
- Kept: Core Law + Simulation substrate + dispatch manifest (now SourceGen) + per-grain journals + N+1 proof feature + capsule/pack/publish/install loop + ino name in vision + brain-owned orchestration + surfaces as synapses + hex1b speed + private contracts + awesome seed + review on real paths.
- Lost (or archaeology only): rich visual authoring UI, decay memory synapses, full dynamic InoLang everywhere, heavy domain silos in the canonical tree, rich clients as primary, full IAW agent base.

## Current State (final, at time of this synthesis)

See final/docs/ROADMAP.md (Phase 0 done + TUI redesign + private contracts; Phase 1: real durability, marketplace Redis, LAN first-class, observe not compute), VISION.md (three-layer: InoLang+BrainOS OSS, DigitalBrain product, GlobalBrain), DISTRIBUTION.md (pipeline + trust ladder L0 hash → L1 Ed25519 → L2 sim-gate → L3 web-of-trust; private contract section), USER-FLOWS.md (D5 routing table, 20 flows), DELETED.md (ruthless record), NEXT-SESSION-PROMPT.md + CONTINUATION-*.md (session handoff style), CONTINUATION-PRIVATE-MARKETPLACE-CONTRACTS.md (completed marker).

High-sev gate (run at synthesis time): `dotnet test ... --filter "FullyQualifiedName~DistributionDynamicHandlers"` (must stay 17+/17 green; no regression on pack/publish/install/N+1/contract/lifecycle/journals).

Fast loop: `dotnet run start.cs` (REPL client + live TUI surfaces). Full: `aspire run` only for hosting/resources.

## Open / Next (see ROADMAP + existing continuation docs)

Real durability (per-grain beyond current JournalStore stub; Redis for marketplace state), LAN two-machine demo (advertised IP + firewall + peer), headless hex1b scenario tests in CI, Creator auto-pack on author, sim-gate default, discovery beacon, living packages (source in capsules behind L2), GlobalBrain, E2E with Aspire.Hosting.Testing two-kernel + Playwright, observability on distribution telemetry.

Archaeology is reference only — do not extend v1/v2/ino/IAW trees. Mine patterns (capsule co-location, Simulation substrate, E2E fixtures, brain-orchestration, creator loop) and apply only inside final/ shape.

This file + the existing docs/ are the handoff for any future agent. Update on every major phase.

(Generated during full root archaeology pass. All claims traceable to the files enumerated above.)
