# DigitalBrain — Continuation Prompts (Distilled for Future Agents)

This is the "how to be an effective agent in final/" companion to PROGRESS-ARCHAEOLOGY.md (the full cross-version synthesis) and the per-session handoff files (NEXT-SESSION-PROMPT.md, CONTINUATION-PRIVATE-MARKETPLACE-CONTRACTS.md, ROADMAP.md, DELETED.md, DISTRIBUTION.md, VISION.md, USER-FLOWS.md).

**Primary rule:** final/ is the canonical clean reboot. All new work happens here. The other folders (v1/, v2/, v3/, v4/, IAW/, ino/, mcps/, final-deleted-*, _hex1b-probe/) are archaeology/reference only — read to recover a pattern or lesson, never extend.

## Session Opening (non-negotiable, every time)

1. `git status` — must be clean (or only expected untracked like this doc). ABORT if dirty without explicit owner approval.
2. High-severity gate (the Core Law proof):  
   `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --filter "FullyQualifiedName~DistributionDynamicHandlers" --logger "console;verbosity=minimal"`
   Must be 17+/17 green (or current count after legitimate additions) with 0 failures. No regression on pack/publish/install/N+1/contract-only/lifecycle/journals/surfaces/awesome-se/review. The filter exercises real Orleans TestCluster + Simulation substrate + timeline stream + SynapseDispatch (manifest or fallback) + per-grain journals.
3. If any hosting/AppHost/resource change is planned: `cd src/DigitalBrain.AppHost && dotnet build && aspire build` (or equivalent). Prefer `aspire run` only when actually touching resources (fast loop is `dotnet run start.cs`).
4. Re-verify codegraph/ if the session prompt requires (some handoffs mandate `codegraph init` + output present).
5. Read (at minimum): this file + PROGRESS-ARCHAEOLOGY.md + VISION.md + DISTRIBUTION.md (incl private contract section) + DELETED.md + ROADMAP.md (current phase) + USER-FLOWS.md (D5 routing table) + the active continuation doc for the thread. Skim the feature file for the exact scenarios in scope.

## The Core Law (never relitigate)

- Everything is a Neuron (INeuron : IGrainWithStringKey, usually via Neuron base) or a Synapse (abstract record with SynapseMetadata + Stamp for lineage).
- Wiring is declared on interfaces: `public interface IFooNeuron : INeuron, IHandle<Bar>, IEmit<Baz> { }`. This is the scannable source of truth for the dispatch manifest and "N+1 after install" proof. SourceGen (DigitalBrain.SourceGen) emits DispatchManifest at build time (KnownHandlers + KnownContracts with IsHandle for private shape-only bundles). SynapseDispatch prefers pre-resolved invokers from the manifest; falls back to reflection over IHandle<>.
- Broadcast timeline (one global stream) + p2p Ask/Reply. RoutingMode lives in metadata, not as a subtype.
- A Simulation is a neuron. Tests fire real synapses into the live substrate and assert on the timeline (Expect). The same path gates AI-authored experiences (Creator + ActionRunSimulation).
- Install grows the brain: InstallBundle → BundleInstalled broadcast → N+1 handlers participate immediately (including contract-only bundles that carry no impl/.ino, only shape decls; sim doubles in the test assembly provide the reacting IHandle<> and dispatch still works via KnownContracts). This is proven exclusively in DistributionDynamicHandlers.feature + Simulation.cs. No other substrate counts for the contract.
- The journal is the truth. Per-neuron Incoming/Outgoing (IDurableList via JournalStore for isolation; fixes earlier cross-grain interleaving). Full causal replay for LlmAgent, Creator, Packager (observedSynapses), self-improvement, debugging. Activated / SynapseIncoming / SynapseOutgoing / Deactivated are first-class observable lifecycle synapses.
- ino is a peer neuron, not a god. It proposes, gates via simulation, installs through the same public synapses.
- Experiences carry evidence (manifest observedSynapses + journal size from real use). Pack from lived journals or authored .ino content.
- Brain (not AppHost) owns real decisions: StartWorld, resource restart (IAspire), Flutter client, launcher seeding of core experiences (awesome-se-team etc).
- No boilerplate `/// <summary>`. Self-explanatory names. Variable/type names must make the intent obvious on first read. Focus code review here.
- Latest packages only (Directory.Packages.props central). Use Context7 (and codegraph, microsoft-learn, etc.) for ALL external API lookups — never local NuGet cache under C:\Users or user profile. Stay inside E:\Projects for all file operations.
- After any change that could affect the law: re-run the high-sev Distribution filter until green. Then run-ci.ps1 for the full suite when landing.

## Architecture Map (final/ — read the actual files)

- **Core (pure, no hosting):** Domain/Events (Synapse + all the vocabulary: Bundle*, Install*, Review*, UiSurface, Weather*, Alarm*, Telemetry, lifecycle wrappers), Application (INeuron, IDigitalBrain with Send/List*/Journals/Install/Publish/StartWorld/Launch resolvers, IMarketplace, IPackager, IUiNeuron, SimulationNeuron, Brain well-known key), Infrastructure/Orleans (Neuron base with journals + depth + dispatch + Emit/Ask/Reply, SynapseDispatch, SynapseStream, JournalStore), State (NeuronState + attr), UI (Widgets.cs: UiWidget union + WidgetTree.Render; Markdown added for reviews/agent; concrete arrays for codec; Row kept in union for replay safety even if no current emitter).
- **SourceGen:** Incremental generator scans IHandle/IEmit (and now KnownContracts shape), emits DispatchManifest (frozen, pre-resolve hooks). Load-bearing for perf + the private contract N+1 proof. Generator regression must fail loud (ManifestAvailable assert).
- **Kernel:** Thin experiences as neurons (MarketplaceNeuron, PackagerNeuron (journals→.brain + SHA + observed), CreatorNeuron, LlmAgentNeuron (tools + review_project on real kernel-local paths with caps), KernelTaskSupervisor (reminders + surfaces), UiNeuron, WeatherWatcherNeuron, MemoryNeuron, TranscriptionNeuron, HexGuide, SurfaceStreamService (gRPC fanout for Flutter only)). DigitalBrainGrain (the IDigitalBrain impl: install logic + subscriber math generalized for contracts + ContractBundles state + activation guard). Program + KernelHost. DigitalBrain/ sub for the grain.
- **Sdk:** IAspire (restart surface + StartDistributedApp etc), MarketplacePeer (world@host:gateway → IDigitalBrainClient via launcher ConnectExisting + cluster client), DigitalBrainLauncher (EnsureDomainExperiences + SeedCoreListings for awesome), Flutter/ shims (some deleted), Windows/FileSystem neuron example.
- **AppHost:** Deliberately thin (env DIGITALBRAIN_*, reflection-loads DigitalBrain.Kernel.KernelHost.RunAsync). Aspire.Hosting extension for ConfigureDigitalBrainDefaults (journals, streams, reminders, setup).
- **Clients:** Console (hex1b TUI: TaskManagerClient + SurfaceRenderer (generic walker over UiWidget) + ClientActions (shared by buttons + slash) + recent TabPanel(Ask|Creator|Market) + InfoBar + Notifications + Rescue; surfaces routed by id prefix per D5 table in USER-FLOWS; Markdown support). Flutter (minimal Dart + gRPC surfaces + web; often off the fast path; E2E kept but skipped for high-sev).
- **Tests:** Reqnroll + Simulation.cs (the substrate) + DistributionDynamicHandlers.feature (the proof) + DistributionSimulationBindings (collector grain + probe for timeline observation + roundtrips + WidgetTree asserts) + NeuronE2ETest (skipped) + ProjectReviewTests (the honest heuristic over real kernel-local *.cs with TODO count + caps).
- **pa-files:** Runtime packages + marketplace capsules (.brain zips: manifest.json + experience.ino or pre-shipped activation like awesome). .gitignore covers generated.
- **start.cs + run-ci.ps1:** Fast REPL entry (file-based, per-world cluster, ollama gemma default, seeds via launcher, launches TUI). CI is build + high-sev filters + full suite.

See CLAUDE.md (root) for the big-picture layering and "dispatch manifest is the performance + provability seam".

## Archaeology Lessons (from PROGRESS-ARCHAEOLOGY.md — read it)

- v2/ (clean-room): The manifesto that defined the law. Capsules (Contracts + Sims + impl co-located), Simulation base (Fire/Expect), Creator (architect/implementer/gate + LLM via synapses), .ino lowering (neuron/using/handles/broadcasts/on:/scenario/ui:), Catalog from interfaces, many bundle projects, 5/5 sims green. Directly ancestor of final's Core + Kernel + feature + pa-files + SourceGen.
- v1/: Product ambition (Flutter visual constructor + Ino editor + living canvas + constellation, brain controls Aspire resources, rich InoLang dynamic + Roslyn everywhere, heavy SDK connectors). Pain (blank screens, 14k line legacy bloat, complex overlapping test harnesses) drove the cuts that enabled the clean reboot. IDigitalBrain orchestrator idea survived (now IAspire + brain-owned).
- ino/ (on IAW substrate): "ino" product name + assistant vision. Strongest E2E (NeuronE2ETest + Playwright over generated RFW/UI + multi-silo fixtures + BDD). Synapse as signal+memory(decay)+thinking(code). Multi-domain silos (kernel/identity/travel/taxi). L1/L2/L3 self-improvement. Rich clients (flutter with Rive persona orb + Telegram). E2E/BDD/multi-world patterns mined; rich clients and full substrate kept as archaeology (final fast path = start.cs + hex1b + Reqnroll proof only).
- IAW/: Rich hosting (Orleans Agent base with Journaling/state/tools/approval/streams/scheduling, multi-LLM/voice/Qdrant/Ollama tiers, observable traces, DevUI/Telegram/MCP). Informed experiences + live behavioral verification culture (use MCP `assistant_chat` / `agent_get_events` for agent work; final uses Reqnroll + collector for the law proof).
- v3/: v2 + explicit capsule layout + transpiler sims (transitional).
- v4/: Fresh Aspire scaffold (AppHost + Blazor web + api + abstractions for bundles/tasks). Bridge to thin AppHost.
- mcps/: Shared tool defs (always use context7 for APIs before code; playwright/dart/codegraph etc). Dups were deleted from some trees.

**Dead-ends to avoid re-learning (see DELETED.md for the full ruthless list + owners):** Full per-neuron Flutter UI + living canvas (cut 14k+), gRPC client transport as primary (timeline is the one universal client transport; gRPC only for flutter surfaces), usage.json in capsules (written never read), GetListingAsync, global peers state early, llm-chat junk surface, heavy duplicated Verify inside every sim scenario, client-side kernel-tasks install on every run, committed pa-files capsules (runtime generated), mcps/ dup inside final/, full flutter client + AppHost resource (fast path doesn't need it), Row emitters in most surfaces (union case kept for replay), FakeDigitalBrain, complex SimulationSpec, Signal as subtype, MapCatalog, etc.

**Kept for safety:** Row in UiWidget union (journals may contain serialized instances; removing [GenerateSerializer] case is wire-format break). Lifecycle synapses as first-class. Per-grain journals (isolation via JournalStore).

## Process (Elon's 5 Steps / minimalism spirit used in this repo)

1. Question the requirement explicitly (trace to owner; write the answer down).
2. Delete ruthlessly first (if you're not adding ~10% back later, you didn't delete enough). Record in DELETED.md with owner.
3. Only then design/implement the minimal delta that still proves the law.
4. Verify with the high-sev gate + relevant headless TUI or collector roundtrips.
5. One logical commit (message lists deletions + references the continuation doc). Update docs (DELETED, USER-FLOWS, ROADMAP, DISTRIBUTION, this file if lessons generalized). run-ci.ps1 green before land.

For UI/TUI work: pull hex1b repo .claude/skills + samples + AGENTS.md first. Verify widgets against installed 0.164.1 (or current pin) via probe. Shell chrome (TabPanel, Notifications, InfoBar, Rescue) vs union (only what neurons must emit; Markdown was the scenario-forced addition). SurfaceRenderer generic. D5 routing table is load-bearing.

For distribution/market/private contracts: all proofs exclusively through Simulation.cs + Reqnroll feature (no new real AppHost multi-silo required for the core contract). Reuse InstallBundle/BundleInstalled + existing IMarketplace/IPackager + peer machinery. Extend manifest for IsContractOnly + ContractHandlers; ListSubscribers/ListActive generalize for contract contrib; no impl activation for contracts.

Never add features "just in case". Every change must be justified by a named user flow or the flagship demo (2-machine LAN pack-from-lived or authored → publish → peer install → N+1 growth → use (weather, review C# project at real kernel-local path, reminders as widgets, etc.)).

## Landmines (do not relearn)

- Orleans serialization: concrete arrays (UiWidget[] not IReadOnlyList) on records that cross the wire or go into journals. Test new union cases through collector + probe roundtrip before depending on WidgetTree.Render output.
- Silo-wide shared journals (current Phase 1 item): GetRecentHistory ordering can be flaky by design. Do not write new assertions that depend on perfect cross-grain ordering or per-grain isolation until the real durability work lands.
- Dispatch manifest is load-bearing: any new IHandle<> or contract wiring must be picked up by the generator (or the reflection fallback still works and the test proves it). Assert ManifestAvailable in critical paths.
- "account-b" / different grain key on the same TestCluster = established "second user / peer" simulation. Use it for cross-account install scenarios.
- Awesome (SE team) cannot reference SurfaceStreamService (direction of project refs); review surfaces travel the Orleans timeline only. TUI subscribes to timeline so unaffected. Surface fan-out belongs in Emit/DurableNeuron, not call sites.
- No new top-level grains for private contracts or similar — everything stays INeuron + IHandle/IEmit so dispatch and the N+1 proof continue to work uniformly.
- Fast path (start.cs + targeted test filter) is sacred for inner loop. aspire only for hosting/resources. run-ci.ps1 for landing.
- Context7 (and friends) before any API use. Latest packages. Relative paths inside final/.

## Definition of Done (typical session)

- git status clean at start + high-sev Distribution filter green at start and end.
- The named requirement (from Step 1) is implemented with minimal delta.
- All affected scenarios in the feature (or new ones) pass; no regression on the 17+.
- Relevant docs updated (DELETED for cuts, USER-FLOWS for new flows, ROADMAP if phases shift, DISTRIBUTION for contract shape, PROGRESS-ARCHAEOLOGY if new lesson, this file or the active continuation if session-specific).
- One clean commit (or small logical stack) whose message references the continuation doc.
- For TUI: headless scenario test(s) via hex1b input sequences + screen assertions (or WidgetTree + collector for surfaces); one real manual run confirming live stream delivery without input.
- For hosting: aspire build + short smoke if touched.

## Quick Commands

- Fast: `cd final; dotnet run start.cs`
- High-sev gate: the Distribution filter above.
- Full CI: `.\run-ci.ps1` (from final/ or root as appropriate).
- Targeted: `dotnet test ... --filter "FullyQualifiedName~YourScenario|CommandRouter|ProjectReview"`
- Aspire (when needed): `aspire run` (from AppHost or via config); `aspire ps`, `aspire logs`, `aspire stop`.

## When in Doubt

Re-read the Core Law section above + VISION.md "Core Laws" + DISTRIBUTION.md "Thesis" + DELETED.md (one line per deletion + owner). Question whether the concept can be expressed as neuron↔synapse. If not, it does not belong in core.

The flagship demo (two-machine LAN pack/publish/install + use by the receiver's ino/LLM) is the bar. Everything serves that first.

This prompt + the archaeology doc + the handoff files are the institutional memory. Update them when you learn something that would have saved time on this pass.

(Generated as part of the full root archaeology pass that produced PROGRESS-ARCHAEOLOGY.md. All prior session handoffs and code were cross-referenced.)
