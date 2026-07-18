# Detailed Plan: Self-Improving DigitalBrain Loop, File/Syntax Neurons, UI Neuron Kit, Full Product Vision, and Minimal Console MVP POC (ASAP)

## Context
The user wants to "close the self-improving loop" in DigitalBrain by leveraging the neuron/synapse primitives (and v2 clean-room model) for deep code/project awareness and runtime extension:
- Create a **synapse per file** in the project + **tree structures on folders** + **syntax trees on code** (leveraging existing Roslyn neuron + workspace/callgraph/inheritance in `DigitalBrain.Microsoft.Roslyn` and Ino context providers). This enables "domain oriented search" (`tool_digitalbrainSearch`) and per-neuron self-understanding ("You're {Aspire} neuron in digital brain - space consists of neurons (logical pieces) and synapses (data contracts)").
- **Each neuron (aim 500-2000)** exposes its **own understanding** via enhanced "Definition Card" (schema, telemetry.Metrics/Logs, core tools like `GetState()`, `GetTelemetry()`, `GetSchema()`, state.incoming/outgoing, private/functional tools/handlers).
- **Map definition cards + tools to .ino files**: Core inherited tools (GetState etc.) vs. neuron-specific `on SynapseName:` handlers (private tools). Implementations live in SDK (Software 1.0 C# sidecars/bundles) **or** created on-the-fly via .ino (Software 2.0) which "handles request and coding capabilities" on awesome (sw10 old C# / sw20 .ino). .ino reuses SDK sw10 or ships private impl via contracts (marketplace premium bundles with private .cs sidecars).
- **Awesome + Ino role**: Awesome (sw10/sw20 neurons/synapses defining workflows for old/new paradigms) is available in marketplace. Ino (pre-installed from marketplace) demonstrates generating new UI, neurons, etc. Think of BAs authoring .ino for bundles.
- **Key product features**: Sharing neurons/synapses = sharing experiences (GH community for kernel/ino/ask/awesome + marketplace for private). Kernel/ino etc. can be opensource repos. Private companies keep private software (valid via private marketplace). 
- **Main technical challenge**: Generate new neurons + synapses + test scenarios **on demand at runtime**, then pack + distribute (via aspire hosting integration as "nuget-like" self-contained packages/bundles that define synapses + can `IDigitalBrain.Fire` next synapses). Neuron may have built-in LLM or use shared keyed clients.
- **UI vision (Apple-like presentation of full product)**: DigitalBrain = **OS + private self-hosted cluster**. Download "software" as aspire bundle packages (hosting integration + synapses covering real software + IDigitalBrain usage). Marketplace = private software. Spatial glassmorphic constellation canvas (neurons as nodes, active synapse "comets" animating live). Open-source **UI neuron kit** (Button, Globe, Cloak, Calendar, Layout neurons etc. with pre-defined RFW/.ino ui: + their tap/state synapses). Auto-generated UI for scenarios or "visualizing currently active synapses" (until update). RFW UI-as-data from .ino `ui:` blocks (state bindings + events=synapses) or C# sidecars. Definition cards rendered as rich cards in UI. Full product feel: minimal high-fidelity spatial glass (frosted, refraction), live pulsing/comets, neuron cards showing schema/tools/telemetry, Ino chat driving installs/gens, extensibility where installed experiences instantly react to system broadcasts.
- **Distribution**: Free (GH `digitalbrain-domain` clones, implicit registry) vs. Premium (signed .bdom zips + license tokens via Stripe, frontmatter `@price`/`@requires`/`@license`, verified install gate + scenario green). Bundles self-contained (include sims for gate?); experiences bundle contracts/workflows/features(gherkin)/sims/verdicts for verification + sharing. Aspire packages easy self-contained (IBundle + hosting ext + control synapses like StartDistributedApp).
- **Simulations vs Experiences**: From code, experiences bundle sim *specs* (for install gate/verification/BDD contracts, not always executable live). Sims can be separate "experience" or co-packaged. Need to support "run simulation from the app" (e.g. via Ino or UI surface triggering sim runner). Brainstorm: co-locate for distribution simplicity (one artifact to verify+ship), but expose via discovery index + IDigitalBrain for runtime run/observe (different "experience" or capability).
- **File/domain neurons**: CSharpFile neuron (abilities specific to C#/Roslyn: syntax, members, compile check). Other domains (e.g. "Imaple"?) have different. Folder tree as hierarchy of neurons/synapses. Enables self-awareness + better authoring in loop.
- **MVP goal (super minimal but working, test ASAP in console chat)**: Console chat (enhance Ino.Console) with **prompted suggestions** (e.g. "press 4 to install experience from marketplace"). "Install" (via chosen path) adds reactive neuron/synapse. Newly installed **must react to broadcasting of existing running system** (crazy extensibility demo: e.g. fire known synapse like one from context or broadcast, new neuron handles/emits observable response on timeline). Verify full loop: self-gen via creator + install-like reg + reactivity + UI surface update + definition card. Use v2 creator/awesome ino authoring + direct reg per answers. Include basic file/folder indexer. Both console UI enhancements + sample UI neurons. Document full Apple-like vision + RFW mapping. Quick POC, no full prod polish (defer durable, federation, stripe real etc. if not core to demo).

This builds on existing (v1 full: neurons/synapses/ino/awesome/marketplace/roslyn/aspire bundles/presentation UiSurface + definition descriptors; v2 clean-room minimal + recent creator/llm integration + plan in structure.txt). Closes self-improving (Ino/Awesome authoring gated sims → runtime neurons → share via bundles/marketplace → more awareness via file/syntax neurons → better gens).

## Recommended Approach
**Overall architecture (unify v1 + v2 vision, "everything neuron/synapse")**:
- Core remains Neuron (grain) + Synapse (record + metadata routing: Broadcast/P2P via Emit/Ask/Reply + Fire on IDigitalBrain).
- **Enhanced per-neuron self + definition cards** (extend `BrainNeuronDescriptor` + `DescribeNeuronAsync`/`DescribeSelfAsync` + `IBrainSelfModel`): Add "You are X neuron..." persona text, core tools (GetState/GetTelemetry/GetSchema as inherited facets, implemented via durable state + OTel + reflection on IHandle/IEmit), private tools = the `on <Synapse>:` handlers (listed in capabilities with synapse schemas). Map 1:1 to .ino (ui: for card rendering + handlers as `on` blocks; state/telemetry as `state` + @telemetry). Cards serializable for RFW/JSON surfaces or LLM context.
- **File/syntax tree for project awareness + self-improving** (new or extended):
  - Extend Roslyn neuron (or new `ProjectFileSystemNeuron` / `CSharpFileNeuron` in Roslyn bundle or SDK) : On `LoadWorkspace`, walk solution/projects, for each .cs file emit `CSharpFileSynapse` (Path, ContentHash, SyntaxSummary {Members, Usings, etc.}, FolderPath) + build folder tree synapses (FolderNode {Path, Children, FileSynapses}).
  - `CSharpFileNeuron` (one per active file? or keyed) has domain abilities (Analyze, Refactor, CompileCheck specific to C#) + self "You're CSharpFile neuron...". Other domains get analogous (e.g. for .ino files).
  - Use for `tool_digitalbrainSearch` (domain search over file synapses + trees + syntax + existing neurons/synapses via catalog + timeline history).
  - Feeds Ino context (already has Roslyn provider) + Awesome for better code-aware authoring/gens.
  - Tree: synapses carry parent/child refs or use separate hierarchy synapses; visualized in UI constellation or list.
- **Software 1.0 / 2.0 + generation + sidecars**:
  - sw10: Hand C# (SDK bundles, sidecars, private .cs in premium bundles).
  - sw20: .ino (high-level neuron decl + using/broadcasts/handles/state/on emit/ask + ui: + scenario; lowers to sw10 via InoLang parser/transpiler/Roslyn + sidecar pattern for mixed).
  - Runtime gen (close loop): Ino requests "author" (via capability) → Awesome loop (Architect plans from catalog/now + file/syntax context; Implementer authors .ino text via LLM or template, using sw10 SDK reuse or new contracts; Reviewer gates with on-demand Roslyn compile + scenario exec via InoScenarioRunner + sim run) → register (AuthoredNeuronRegistry or InterpretedNeuronRegistry) → hot InterpretedNeuron (or compiled ALC) handles.
  - Pack/distribute: .ino + optional .cs sidecars + manifest (frontmatter @requires @price) → zip/bdom (or git free domain) → publish (signed) → install (verify sig/license/requires/contracts, re-gate compile/scenario, register interpreted or experience contracts/sims). Aspire bundles: IBundle + hosting ext (self-contained, defines synapses + can Fire via injected IDigitalBrain).
  - Private tools vs core: Core always available (inherited in Neuron base or via facet grains); private = specific handlers (declared in contracts for catalog wiring). In .ino: core via special syntax or state, private as on: .
- **UI open source kit + auto surfaces / active synapses / definition cards (Apple presentation feel)**:
  - **Open source kit**: Standard neurons (e.g. new small `DigitalBrain.UiKit` or in Presentation/SDK; or v2 style). 
    - `Button : Neuron, IHandle<Tap?>, IEmit<TapEvent>` (or generic intent); pre-defined ui: or RFW layout.
    - `Globe, Cloak (DPAPI encrypt), Calendar, Layout (reacts to StateChanged synapse for reflow)`.
    - They are installable bundles (sw10 or sw20), appear in catalog, fire real synapses on interaction.
  - **Auto UI + visualization**: Enhance existing `UiSurface` (synapse) + `ConsoleUiSurfaceRenderer` (and future Flutter) to auto-generate surfaces from neuron descriptors (definition card as "Panel" with schema/telemetry/tools list; "active synapses" list or live comet viz until update; scenario status). Buttons in card fire `SendSynapse` (tool) or specific private tool.
  - **RFW / .ino mapping to cards**: `ui:` block in .ino (or equivalent C#) declares UiKit trees bound to state (e.g. lastSeen) + events (Button action → synapse). Compiles to JSON (UiLayoutJson or UiSurface). Definition card fields (core + private) map to panels/cards. Spatial: constellation (from CatalogNeuron) with nodes (neurons + self cards), edges (from IEmit/IHandle), live comets (timeline observers animating in-flight synapses).
  - Full product: Glassmorphic (frosted panels, keylines, Inter/Outfit fonts, 32px grid), live pulsing (busy states via amber rings), Ino chat + numbered suggestions (install, gen, search files), surfaces pop in chat or dedicated view, install instantly adds neurons that react (e.g. new UI button appears or new file neuron indexes and self-describes).
  - Qs addressed (per vision): UI kit in C# SDK sidecars (uniform, BAs write .ino for them; gRPC roundtrips ok for now) or Dart for perf (hybrid: C# declares, Dart RFW custom for widgets). Sandbox later for premium .cs sidecars (ALC is start).
- **Distribution + extensibility (crazy react on broadcast)**:
  - Free: GH clones (domains), LocalDiskBundleSource + discovery at boot.
  - Premium: signed bundles + license + `LocalBundleInstaller` + `InterpretedNeuron` (or experience installer for contracts/sims).
  - Aspire "nuget": Self-contained (bundle + SDK hosting + synapses for control like StartApp/RestartResource + IDigitalBrain usage for firing). Easy: reference SDK, implement minimal IBundle (or empty), use AddDigitalBrain* in AppHost.
  - Sims in experience pkg: Yes for distro (one artifact, verified at gate/install via InoScenarioRunner or sim runner; see ExperienceSimulation + verdicts). But for "run from app": Expose via `ISimulationDiscoveryIndex` + new capability/synapse (e.g. `RunExperienceSimulation`) on Ino/brain; separate "verification experience" vs "live sim experience". Co-packaged is good (gate + runtime use same spec).
  - Reactivity: Installed (static bundle or interpreted) neurons auto-sub to timeline/streams for their IHandle<> (declared in contracts or .ino broadcasts/handles). Broadcast existing synapse → new neuron receives if matches → handles/emits (observable on timeline/UI). No central broker.
  - Sharing: GH for opensource (kernel/ino/ask/awesome/UIkit + community neurons/synapses); marketplace for private/premium (still valid biz). Kernel opensource separate from marketplace.
- **Ino as BA/team + LLM**: Ino (with context providers for self/file/syntax/catalog/timeline) + awesome team neurons drive authoring. Pre-installed ino from marketplace shows gen new UI/neurons. Bundles authored by "team of BAs" (ino + feature text).
- **Built-in LLM?**: Not every neuron; use keyed `IChatClient` (from aspire/ollama/xai bundles, injected). For gen (Implementer) or per-neuron (optional sidecar Llm neuron). For strict JSON use reasoning model.
- **MVP POC scope (super minimal, console chat, ASAP)**: 
  - Run in Ino.Console (or small harness using kernel host + IDigitalBrain + timeline watch; reuse live substrate for tests).
  - Chat with Ino (existing), but add **numbered suggestion prompts** (hardcoded or from context: 1. search files, 2. describe self/neuron, 3. author new via creator, 4. "install" experience from "marketplace").
  - On "4": Use v2 creator/awesome (ino authoring + direct registration per user choice) to "install" a generated neuron (that handles a common broadcast or existing synapse e.g. from context/timeline, emits observable like ContextChanged or custom Response).
  - Then demo: broadcast the trigger synapse (via chat cmd or auto), print on timeline + "new neuron X reacted/handled + emitted Y".
  - Include **basic indexer**: Extend/enhance Roslyn neuron (or add simple `FileTreeNeuron` / use existing LoadWorkspace + emit CSharpFileSynapse/FolderSynapse on load; simple tree in state or via synapses). On chat "load workspace" or auto, show file tree or "search" returning file synapses.
  - **UI**: Both - enhance Console renderer + UiSurface to auto render definition card (from DescribeNeuron + self text + tools list + active synapses viz) + simple "scenario/active" surface. Implement 1-2 sample UI kit neurons (e.g. ButtonNeuron that emits TapEvent on "tap" intent; Layout that listens StateChanged) - wire as bundles or dynamic, show in surface.
  - Document full vision (Apple spatial glass + comets + cards + ino ui: mapping to cards + sw10/20 + sharing + aspire pkgs) + reference structure.txt + E:\digitalbrain for flutter details.
  - Self-contained: Use existing bundle paths + direct reg (simulates install). No real stripe/git for MVP (mock "marketplace" via local bytes or in-mem).
  - Verify extensibility: New "installed" reacts without restart.
  - Use main code (full awesome/ino/marketplace/roslyn) + v2 creator patterns/docs as model (v2 for minimal arch purity). Tests via existing Reqnroll + new console BDD or manual run.
  - Quick: Leverage existing (creator loop, Interpreted/ dynamic reg paths, UiSurface, Roslyn, IDigitalBrain, Ino chat, bundle manifests). Add minimal new: file synapse records + indexer logic in Roslyn or new small neuron, card enhancements, sample UI neurons, suggestion menu in console, one demo .ino neuron for "install", wiring in chat.
  - No: full spatial glass (console only), real LLM key req (canned ok), durable journaling, full marketplace UI.

**Tradeoffs considered**:
- v2 pure vs main full: Main for realistic demo (has marketplace/awesome/roslyn/install paths); v2 for arch guidance + recent creator work.
- Full indexer now vs doc+stub: User chose include basic (shows idea live).
- UI kit impl now: User chose both (enhance surfaces + samples) + doc vision.
- Install path: User chose v2 creator + direct reg (ASAP, avoids heavy deps for demo; still shows authoring + "install"=reg + react).
- Sims bundling: Document co-packaged for distro/gate + runtime run via discovery + new synapse/capability.
- Private tools: Core always (inherited facets); private = handlers (declared for catalog + .ino on:). Map explicitly in card.
- Sandbox for sidecars: Note for future (ALC start; full AppDomain/process later).
- LLM per neuron: Shared keyed clients (injected); optional per-neuron Llm wrapper neuron.

**Critical files to modify/create (in E:\OrleansExamples)**:
- Main: `Ino.Console/Program.cs` (suggestions menu, numbered 4 install, file tree cmds, auto surfaces).
- `DigitalBrain.Microsoft.Roslyn/Microsoft/Roslyn/Roslyn.cs` + `IRoslyn.cs` + synapses (add file/folder synapse handling + indexer on load; emit CSharpFileSynapse etc.).
- New/ `SDK/DigitalBrain.UiKit/` or `DigitalBrain.Presentation/UiKitNeurons.cs` (or in existing): sample ButtonNeuron, LayoutNeuron (with synapses, self desc, ui surface).
- `DigitalBrain.Presentation/UiSurfaceContracts.cs` + `UiSurfaceRendering.cs` (enhance auto card from descriptor + active synapse viz; support definition card fields).
- `DigitalBrain.Core/Runtime/Reflection/BrainReflectionContracts.cs` (enhance descriptors for "self understanding" text, core tools list, private tools).
- `DigitalBrain.Kernel/DigitalBrain.cs` + contracts (enhance Describe* to include richer card, file context if wired).
- `Ino/Context/` (add/enhance file/syntax + card provider; update SelfDefinitionContextProvider).
- `DigitalBrain.Awesome/` or `Ino/` (tie file context into creator for better gens; ensure sw10/20 .ino reuse SDK).
- For demo bundle: embed minimal .ino in test (or use v2 creator output); direct reg path (e.g. extend InterpretedNeuronRegistry or use v2 Catalog/Creator sim style for POC).
- v2/ (for reference/docs): no or minimal edits; use as model.
- Docs: Update `docs/vision/*.md`, `v2/docs/structure.txt` or new, `ORIGINAL_REQUEST.md` if needed; add to plan/ .
- `DigitalBrain.Marketplace/Experience/` + `Local*Installer` (minor if needed for sims co-bundle note).
- New synapses/records: e.g. in Roslyn or Core: `CSharpFileSynapse`, `FolderTreeSynapse`, enhanced `NeuronDefinitionCard : Synapse`.

**Existing functions/utilities to reuse (with paths)**:
- Neuron base + Fire/Emit/Ask/Reply + State + Logger (v2 `v2/src/DigitalBrain.V2.Core/Runtime/Neuron.cs`; main `DigitalBrain.Core/Runtime/Neuron.cs`).
- IDigitalBrain.Fire + client Send/WatchTimeline ( `DigitalBrain.Kernel.Contracts/Brain/IDigitalBrain.cs`, `DigitalBrain.Kernel/DigitalBrainClient.cs`, `DigitalBrain.Kernel/DigitalBrain.cs`).
- DescribeNeuron/DescribeSelf + BrainNeuronDescriptor/BrainSelfDescription + capabilities ( `DigitalBrain.Core/Runtime/Reflection/BrainReflectionContracts.cs`, `DigitalBrain.Kernel/DigitalBrain.cs:178`, `IDigitalBrain.cs:30`, `Ino/InoBrainSelfModel.cs`, `Ino/Capabilities/InoCapabilityCatalog.cs`).
- Roslyn: LoadWorkspace/AnalyzeSyntax/GetCallGraph/GetInheritanceTree/CompileDraftCode + IRoslyn ( `DigitalBrain.Microsoft.Roslyn/Microsoft/Roslyn/Roslyn.cs`, `IRoslyn.cs`; used in Ino/Context/RoslynContextProvider.cs, Ino/Ino.cs).
- InoLang parse/transpile/scenario run + AuthorDraft/Reviewer ( `DigitalBrain.InoLang/*`, `DigitalBrain.Awesome/Team/ImplementerNeuron.cs` (AuthorDraft emits .ino), `ReviewerNeuron.cs`, `AwesomeCreatorLoop.cs`; `InoScenarioRunner.Execute`).
- UiSurface + renderer + intents + trigger ( `DigitalBrain.Presentation/UiSurfaceContracts.cs`, `UiSurfaceRendering.cs`; `Ino.Console/Program.cs:127` /ui handling; `Ino/Testing/UiSurfaceSteps.cs`).
- Bundles/IBundle/install ( `DigitalBrain.Kernel.Contracts/Bundles/IBundle.cs`, `DigitalBrain.Kernel/Bundles/BundleInstaller.cs`, `DigitalBrain.Marketplace/Marketplace/LocalBundleInstaller.cs`, `MarketplaceBundle.cs`, `Experience/LocalExperienceBundleInstaller.cs`).
- InterpretedNeuron + reg for dynamic sw20 ( `DigitalBrain.Marketplace/Marketplace/InterpretedNeuron.cs`, `InterpretedNeuronRegistry.cs`).
- Catalog for wiring/contracts ( `IContractCatalog`, `InMemoryContractCatalog`).
- Simulation discovery + run ( `DigitalBrain.Core/Simulation/*`, `ISimulationDiscoveryIndex`, SDK simulation).
- Context providers for Ino/LLM ( `Ino/Context/*` including SelfDefinition, Roslyn).
- Aspire hosting ( `SDK/DigitalBrain/Hosting/DigitalBrainAspireHostingExtensions.cs`, `DigitalBrainService.cs`; Microsoft.Aspire bundle + neuron).
- Timeline/stream for reactivity ( `DigitalBrain.Core/Runtime/SynapseStream*`, `Neuron.OnNextAsync`).
- v2 primitives/docs for arch purity ( `v2/src/DigitalBrain.V2.Core/*`, `v2/docs/00-04*.md`, `structure.txt` for definition cards, UI kit neurons, sw10/20, distribution, spatial, ui: RFW, file? vision).
- Ino chat + session ( `Ino/Ino.cs`, `Ino.Console/Program.cs`, `Ino/InoConsoleHost.cs`).

**Verification section (end-to-end test the system ASAP)**:
- **Build/test baseline**: `dotnet build DigitalBrain.slnx --no-restore` (or v2 slnx), existing Reqnroll (SDK\... or full) 64+ scenarios green, specific (Awesome creator loop, RoslynNeuron, Marketplace install, UiSurface, Ino context, Learning self-imp).
- **MVP console POC run** (manual + scripted):
  1. `dotnet run --project Ino.Console` (or harness with kernel host + brain client).
  2. In chat: see suggestions (incl "4. Install demo experience (via creator+reg) that reacts to system").
  3. "load workspace" or auto → basic file/folder synapses emitted (Roslyn enhanced); "search files" or describe shows CSharpFile + tree + "You're CSharpFile neuron..." self.
  4. Trigger 4 or "author+install": runs (v2) creator/awesome (canned or key LLM) to author .ino neuron (handles e.g. a broadcast or ContextChanged, self-desc + emits observable); direct reg (simulates install, registers Interpreted or grain).
  5. Broadcast trigger synapse (chat cmd or auto in demo); observe on timeline print + "New neuron Foo.Bar reacted: handled + emitted BarResponse" (shows extensibility; new one subbed automatically).
  6. UI: surface renders auto definition card (schema from handled, tools list incl GetState + private, self text, active synapses viz or list); interact Button sample (if wired) fires real synapse.
  7. Ino context updated with file/syntax + new card; can ask "what's the new neuron" or gen using it.
- **Automated**: Extend Reqnroll (new .ino or steps in Kernel/Testing or Marketplace/Testing): "Ino chat suggests 4", "select 4 authors+regs demo neuron via creator", "broadcast X", "assert timeline has new neuron emit + no prior handler for it", "UiSurface for its card + active", "Roslyn indexes files → CSharpFileSynapse + tree", "self-desc includes 'You're ...' + tools". Use LiveSubstrateWorld or full host. Cover sw10/20 (generated .ino + any sidecar).
- **Distribution sim**: For "install", optionally exercise bundle zip path or Local*Installer with test .ino (free path); assert reg + reactivity (as in existing ExpertiseDistribution.ino / Marketplace.ino tests).
- **UI/Apple vision**: Console demo sufficient for POC (glass-like panels via Spectre, live updates); document full (spatial comets via timeline observer + animation stub, RFW .ino ui: → card/kit mapping, flutter ref to E:\digitalbrain).
- **Self-imp loop close**: After "install", the new neuron appears in catalog/Describe, can be used in further Ino/creator requests (file context helps code gens), "sharing" via hypothetical bundle export.
- **Edge**: Hot reload if reg versioned; errors in gen feed back; no key → canned; existing system (e.g. Roslyn, Ino) broadcasts still work + new reacts.
- **Full product test feel**: Run console, do install, see reaction + card + file awareness + chat suggestion loop; "feels" like extensible OS where experiences (neurons) instantly join the space and share via synapses.
- Run with `dotnet test ... --filter "Roslyn or Awesome or Marketplace or UiSurface or Ino"` + manual console session. Update vision docs. Use Context7 for any new AI/Orleans/aspire APIs before code.

**Phased execution for ASAP POC (after plan approval)**:
1. Enhance Roslyn + add file/folder synapses + indexer + self desc (minimal CSharpFileNeuron logic).
2. Enhance descriptors + Describe + context providers for "You're X" + core/private tools card.
3. Sample UI kit neurons (Button/Layout) + wire to surfaces; enhance renderer for auto cards + active viz.
4. In Ino.Console: suggestion menu, "4" path using creator + direct reg (demo neuron that reacts), broadcast + print reaction + surface.
5. Tie file context into Ino/Awesome for awareness in gens.
6. Tests + manual console run + doc updates (full vision + mapping).
7. (Post POC) Full bundle install path, RFW real, spatial viz, sims co-bundle + run-from-app synapse.

This delivers detailed vision (Apple OS/cluster feel, sharing, sw10/20, UI kit, file awareness, gen at runtime + pack/dist) + working console POC verifying loop + extensibility (install reacts to broadcasts) + self-imp (file/syntax + cards for better awareness/gens) ASAP. Minimal changes reuse heavily; respects v2 invariants where possible.

## Open Questions / Product Shaping (use ask if needed in exec)
- Sandbox for generated .cs sidecars in premium (ALC today; full isolation?).
- Exact "Imaple" example neuron/domain (assume example other than C#)?
- How deep file synapses (full content or summary? tree as first-class neurons?).
- Bundle sims co-located always, or optional "verification experience" bundle separate from live?
- Flutter kit: Option A (Dart RFW perf) or B (C# uniform) - plan assumes hybrid, document both.
- Built-in LLM: per neuron injected client, or dedicated Llm neuron for all?

Plan ready for approval. Execute after exit_plan_mode. (All exploration read-only within workspace.)
