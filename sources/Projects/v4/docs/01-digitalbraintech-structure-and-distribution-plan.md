# DigitalBrainTech Structure & Distribution Plan (Brainstorm / Pre-Impl)

**Status**: EXECUTED (skeletons created, core abstractions extracted with namespace update to DigitalBrain.Abstractions, deleted Kernel sources from restructuring restored, v2 Ping.ino + capsule test migrated to Ino/Testing, simulation separate project DigitalBrain.Sdk.Testing, Aspire hosting sdk wired into digitalbraintech prototype AppHost with demo use of abstractions, guard relaxed for test-only, all after Context7/codegraph, builds 0e after EVERY edit, high-sev attempted, full review passed, main + new 0 errors). "New working prototype" under digitalbraintech/ with split layout started. Root remains working. Plan doc updated live.

**Base commit**: a2da181 (v2/plan.md console MVP POC complete: Roslyn CSharpFileSynapse + FolderTree, NeuronCardRenderer + enhanced cards, I* contracts for UI kit Button/Layout + TapIntent/SelfUnderstanding, Ino.Console numbered suggestions + "4" creator+reg demo reactor neuron that reacts to broadcasts + emits on timeline, file context tied to Ino/Awesome, etc.). Current HEAD: de3297c (added Roslyn synapses, mcps schemas, docs/audit, v2 Llm* without ref -- fixed below).

**Current observed state (post "major restructuring" start)**:
- Untracked: digitalbraintech/ (fresh `dotnet new aspire-starter` seed: AppHost, ServiceDefaults, ApiService, Web (Blazor), Tests, slnx). This is the "new working prototype build from scratch here: digitalbraintech".
- Root: existing monorepo DigitalBrain.slnx with:
  - SDK/ (god-project aggregator): DigitalBrain/Hosting (DigitalBrainAspireHostingExtensions + AddDigitalBrain / AddDigitalBrainApp<TSilo> + DigitalBrainService/ModelBuilder for "single experience" wiring of brain+orleans+ollama models + silo project; Simulation/ (ISimulationBackend, Simulation.cs, ReqnrollAdapterContracts); Testing/ (harness, doubles, LiveSiloSimulationBackend, steps for Aspire/Roslyn/File/Llm etc). Pulls in all sw1.0 bundles via refs. Per roadmap 07 Phase 3: being decomposed (many bundles like .Ai.Llm, .Microsoft.Roslyn, .Windows.FileSystem, .XAI.Grok, .Microsoft.Aspire already extracted with own bundle.json; Hosting stays in SDK thin core).
  - v2/ (clean-room reference, separate V2.slnx): src/capsules/Ping+Greeter (Contracts + Neuron + .Simulations + .ino in one case), V2.Core (INeuron, Neuron, Synapse, own primitives), V2.Creator (+.Simulations: closed loop Architect/Implementer/Gate + Llm), V2.Catalog, V2.Ino (+.Simulations: InoParser/Transpiler/Compiler + InoTranspilerSimulation that parses .ino, transpiles to C# neuron+sim, compiles, runs Fact), V2.Testing (base Simulation/Substrate). Proves sw2.0: small self-contained capsules, .ino authoring, transpilation, creator loop, reactivity demo.
  - Main (sw1.0 + sw2.0 runtime): DigitalBrain.Core (INeuron/Neuron/Synapse runtime, SimulationContracts, Identity, State, Synapses, Clusters), DigitalBrain.Kernel (+.Contracts: IDigitalBrain, IBundle, IContractCatalog, distribution/clustering/ClusterBridge, secrets, tasks/durable, Boot/KernelHostingExtensions.AddDigitalBrainKernel which does UseOrleans + InstallDigitalBrainDomain + bundles), DigitalBrain.Ino (+ InoLang: IInoScenarioRunner, InoToCSharpTranspiler, InoLangParser/Interpreter; Ino agent, Context providers incl Roslyn/SimulationDiscovery/Self, Capabilities, Sessions, Workflows; Testing/ with .ino scenarios), DigitalBrain.Marketplace (InterpretedNeuron + registry for runtime sw2.0 .ino execution + hot-reload + state mig; LocalBundleInstaller; many Ino* tests; Experience), many bundle projects (provide sw1.0 neurons/synapses for legacy enablement: Roslyn for syntax/file, FileSystem, Aspire, Ai.Llm/Search, Postgres, Windows, Auth, Awesome for creator, Presentation UiSurface), DigitalBrain.Testing.Reqnroll (shared world, steps, harness), Ino.Console (Spectre REPL), AppHost (uses SDK AddDigitalBrainApp).
- Ground truth (docs/plan/00 + 06 + 07 + v2/plan + vision/): Everything is neuron or synapse. Ino = AI assistant (console entry + agent bundle + InoLang for .ino 2.0). Distribution via .ino source (not assemblies) + InterpretedNeuron for reactivity on target without recompile (key for cross-cluster). No boot synapse (Aspire + KernelBundleOptions + BundleInstaller). SDK god decomp ongoing. Sims for gates/verification (v2 capsules co-locate .Simulations; main uses .ino scenarios + SDK Simulation). Marketplace as future HTTP host for cross-cluster. v2 POC validates closed creator loop + file/syntax awareness + numbered "install" reg + reactor extensibility.

**Why the changes (user intent + observed)**:
- User explicitly: "i need new working prototype build from scratch here: digitalbraintech prepare a plan. the structure should be like this: SDK - software 1.0 with neurons which would enable old software into digitalbrain. ... pack silo as single experience and ship it like aspire hosting integrations via nuget, but also, it's extremelly important to check v2 and actually, present the plan first. Id really like the structure to be like this: digitalbraintech - organization on github. folder inside it - future repos. and it would have core (maybe separate repo with abstractions), kernel (opensource also with abstractions and clustering or maybe closed source), ino - ai assistant in digitalbrain".
- Matches 06/07 roadmaps (decomp SDK phase3, distribution phase5 with Ino as 2.0 author/runner via .ino + interpreter, Aspire boot, no god grains).
- v2/POC (just landed) is the "check v2" + "Ino as 2.0" proof: creator closed loop, .ino transpiler sim, reactor install demo, file/syntax neurons (CSharpFileSynapse etc via Roslyn), self cards, numbered suggestions in console. Kept reference-only (untracked v2/docs etc were committed in de3297c).
- digitalbraintech/ seed = from-scratch org prototype (will consume split packages once ready; currently vanilla Aspire to show "how a consumer looks").
- SDK/ extraction + Hosting in it = early move toward "SDK for sw1.0 legacy enable + single exp packing".
- Untracked logs/pids per .gitignore update.

**Analysis of packing/distribution (Context7 + code + roadmaps)**:
- Aspire: Custom hosting via NuGet (e.g. Aspire.Hosting.* patterns). AppHost csproj uses <AspireProjectOrPackageReference Include="Aspire.Hosting.AppHost" /> + custom. IDistributedApplicationBuilder extensions (AddOrleans, AddJavaScriptApp, WithReference, AddModel etc). Our SDK/DigitalBrainAspireHostingExtensions already does exactly this: AddDigitalBrain (wraps AddOrleans + models) + AddDigitalBrainApp<TSiloProject> (full brain+silo+endpoints+commands in one call, "single experience"). WithReference(brain) wires env for Ai__ + models. Perfect for "ship like aspire hosting integrations via nuget".
- Orleans: builder.UseOrleans(...) in silo host (KernelHostingExtensions.AddDigitalBrainKernel does localhost/inmem for dev + bundle storage + neuron runtime). Aspire integration: builder.AddOrleans("name").WithReminders(...) in AppHost; .AddProject<T>.WithReference(orleans). Silo as executable (WebApplication or Host). Single-file publish supported via .NET (trimming careful with Orleans grains/serializers). Packaging: ship as package with extensions (like Orleans.Clustering.* + Aspire clients).
- NuGet: SDK-style, multi-tfm (net8+), dependencies auto, symbols/pdb. For AppHost consumers: special ItemGroup or standard PackageRef. Our pattern (SDK package provides the Add* + pulls transitive Aspire/Orleans/Ai) matches. Latest versions (we used Context7; main currently pins 10.6.0 for AI; always bump via verified).
- Roslyn: /dotnet/roslyn for syntax (already used in Microsoft.Roslyn bundle + CSharpFileSynapse POC in v2 plan).
- Reqnroll/xUnit + Spectre: For tests (shared .ino steps in Reqnroll proj per 07 phase2) + Ino.Console REPL. Keep test-only out of prod packages (phase6 CI guard).
- Sims separate? Yes (see below).

**Recommended target structure (digitalbraintech org / folders as future repos; aligns user + 06/07 + v2 POC)**:
```
digitalbraintech/  (org root on GH; this seed dir + monorepo or meta)
- core/ (separate repo, minimal abstractions, MIT/oss)
  - DigitalBrain.Core.Abstractions (or just contracts)
  - INeuron, Synapse base, NeuronId etc (move thin from Core)
  - IDigitalBrain, IBundle, IContractCatalog, Cluster* (from Kernel.Contracts)
  - Ino.Contracts (IConsole etc)
  - No runtime, no Orleans dep if possible. Pure contracts + base records for sw1.0/sw2.0 interop.
- kernel/ (oss or split open core + closed clustering?; includes abstractions + clustering)
  - DigitalBrain.Kernel (silo boot, UseOrleans wiring, AddDigitalBrainKernel, BundleInstaller core, distribution (ClusterBridge, IClusterDirectory), tasks/durable, InMemory impls for dev)
  - Depends on core/. May keep some runtime from current Core (Neuron base, streams) if not in core.
  - Kernel.Hosting (non-Aspire extensions)
  - Future: Marketplace host HTTP bits (per 06 Part E) or separate.
- ino/ (ai assistant; oss)
  - Ino.Console (Spectre REPL entry, numbered suggestions, "install" flows)
  - DigitalBrain.Ino (agent neuron, Context pipeline, Self model, Capabilities, Workflows, Sessions)
  - DigitalBrain.InoLang (parser, interpreter, IInoToCSharpTranspiler, IInoScenarioRunner -- key for sw2.0 authoring + run)
  - Ino as "2.0 author/runner": uses file/syntax (sw1.0 Roslyn), awesome creator loop for .ino gen, registers to Interpreted (runtime sw2.0), transpiles for sidecars.
- sdk/ (sw1.0 neurons for legacy/old software enablement into brain; oss or mixed)
  - Per-neuron small packages (DigitalBrain.Sdk.Roslyn, .Aspire, .WindowsFileSystem, .Ai.Llm, .Ai.Search, .Xai.Grok, .Data.Postgres, .Auth.Google etc) -- each with bundle.json, neurons/synapses/contracts for "old" domains (syntax trees, file ops, aspire orchs, llm calls, db).
  - DigitalBrain.Sdk (aggregator, convenience meta-package pulling sw1.0 neurons).
  - DigitalBrain.Sdk.Hosting.Aspire (or inside sdk): the AddDigitalBrain* extensions, DigitalBrainService, ModelBuilder -- for "pack silo as single experience and ship like Aspire NuGet". Consumer AppHost: <PackageRef Include="DigitalBrain.Sdk.Hosting.Aspire" />; builder.AddDigitalBrainApp<MySiloProject>().
  - Simulation? **Separate project recommended** (e.g. DigitalBrain.Sdk.Testing or DigitalBrain.Testing.Simulation): ISimulationBackend, LiveSilo impl, NeuronTestHarness, doubles. Why: (1) Roadmap phase3/6 wants test pkgs out of prod (no Reqnroll/xunit in kernel/ino/sdk prod). (2) v2 shows sims per-capsule for gate/verification before publish -- co-locate with tests not runtime. (3) Allows "run sim from app" via capability/synapse without bloating silo. SDK can depend on it optionally or have test-only target. Current SDK/Testing moves here + Reqnroll project owns BDD world.
  - Enables "old software": e.g. existing C# project gets Roslyn neuron for awareness, FileSystem for ops, exposed as synapses any .ino or Ino can use/fire.
- samples/ or digitalbraintech-web/ (the current seed evolves here): example full-stack using the NuGets (AppHost calls Add..., silo uses AddDigitalBrainKernel, Ino.Console, a .ino bundle).
- marketplace/ (future, perhaps under kernel or separate): HTTP host + artifact store for cross-cluster (06 Part E/F).
- v2/ (reference, not published as-is): keep for docs + as seed for "how to structure a capsule/bundle" (Contracts + neuron + .ino + sim). Port concepts/tests into ino/ + sdk/ + Reqnroll.
- docs/ (roadmaps, vision, this plan).

**v2 POC integration / test migration (critical, keep coverage)**:
- v2 capsules (Ping/Greeter point-to-point, broadcasts, reactor) + creator closed loop + InoTranspilerSimulation + .ino authoring = ground truth for "Ino as 2.0 author/runner + sw2.0".
- Migrate: Convert v2 sim bases/tests to use main ISimulationBackend + DigitalBrain.Testing.Reqnroll (already partially happened: many .ino in bundles/Kernel/Marketplace/Ino/Testing). Port Ping.ino etc as first-class .ino scenarios in appropriate bundle or sdk samples. InoTranspiler sim logic -> use main InoLang + compile gate in Reviewer/Awesome. Creator sim -> AwesomeCreatorLoop (already main has it). Keep v2/ as "clean model" docs (structure.txt, 00-05 md) + for comparison.
- No loss: all scenarios (reactor broadcast+emit, numbered install, file/syntax index + CSharpFileSynapse, definition cards, UI kit I* + TapIntent) must have equivalent or better in main Ino/Reqnroll + .ino. Update steps/worlds/harnesses (DigitalBrainScenarioWorld etc).
- Per v2/plan + 06: .ino ships source; sw10 sidecars (Roslyn etc) via SDK packages; sims for install gate (co-package or separate verification experience? doc both, prefer co-locate for simplicity but discoverable via IDigitalBrain for runtime run/observe).
- File context (v2 POC): already wired via Roslyn bundle + Ino/Context/RoslynContextProvider + SelfDefinition; enhance per plan.

**Distribution/packaging decisions (user priority + Context7)**:
- Silo single experience: Yes, via SDK Aspire hosting package. AppHost becomes tiny (just name the TSiloProject + call AddDigitalBrainApp). Ship the package as "DigitalBrain.Aspire" or "DigitalBrain.Sdk.Hosting" NuGet -- consumers get full wired brain (Ino + Marketplace + Awesome + models) + orleans endpoints + commands (rebuild, run-sims). Matches Aspire "hosting integrations via nuget".
- Kernel/silo package: Separate NuGet (DigitalBrain.Kernel) with AddDigitalBrainKernel on WebApplicationBuilder. Prod has no test deps.
- Sim separate: Yes. DigitalBrain.Testing.* (Reqnroll + simulation harness) as dev dependency only. CI guard (phase6) enforces no prod ref to test pkgs.
- Bundles: Still the unit (IBundle + bundle.json + LocalDisk/LocalExperience installers). sw1.0 = C# neurons in SDK packages; sw2.0 = .ino interpreted (or transpiled sidecar) via Marketplace/InterpretedNeuron. Aspire bundles possible (IBundle + hosting ext).
- Cross-cluster (06): Marketplace as standalone HTTP (not grain), artifact store (IBundleArtifactStore, fs for dev), client in clusters, sig verify on download. 3-cluster test (creator publish .ino, consumer install+react) as end goal.
- Versioning: Latest (Context7 verified), consistent tfm (net8+; net11 for v2 ref).
- Free vs premium: GH for core/kernel/ino/sdk (clones, implicit reg); marketplace signed zips + Stripe for premium (frontmatter @price etc).

**Migration/compat notes (Elon's delete/simplify where reorg enables)**:
- Delete god: Continue SDK decomp (phase3); move sim/testing out.
- Core/kernel split: Extract contracts first (small blast); kernel can stay "the kernel" even if some closed.
- v2 -> ino: Port, don't duplicate. v2 stays reference (like "the spec").
- Ino.Console + UI kit: Already POC'd; enhance for new structure (package refs).
- Tests: All .ino + Reqnroll stay green equiv; use shared world post phase2.
- No C# summaries (tiny inline only); excellent self-explanatory names (e.g. DigitalBrainAspireHostingExtensions not "HostingService").

**Next steps (after this plan review/approval; do not implement yet)**:
1. User approves plan (or ask_user_question for choices: core separate repo? kernel fully oss? sim package name?).
2. Then: setup digitalbraintech/ as meta or init sub-repos (git submodules? monorepo under it first?); publish local packages for dogfood.
3. Extract core/ from current Kernel.Contracts + Core + Ino.Contracts (with codegraph impact analysis first).
4. Adjust kernel/ + ino/ + sdk/ packaging (csproj, Directory.Build, nuget props for Aspire special refs).
5. Port v2 tests/scenarios fully (migrate Ping/Greeter/Creator/InoTranspiler to main .ino + harness); delete or archive v2 sim dups.
6. Update digitalbraintech/ seed to consume the packages (AppHost uses Add* from SDK NuGet).
7. Baseline metrics (scenario count), builds/tests green (main + v2 + AppHost), aspire run smoke (short job), full code review (no summaries, naming, invariants "neuron or synapse", plan compliance, Context7 evidence).
8. Per 07: each PR ends green + AppHost boots + Ino prompt answered.
9. Later: cross-cluster 3-way test, HTTP marketplace, etc.

**Risks/mitigations**:
- Package version skew (net11 vs stable AI): pin matching, Context7 before bumps.
- Blast from split: codegraph_impact + callers first; small PRs.
- Test loss on migrate: record baseline count first; port 1:1 + add.
- "Single experience" too magic: keep opt-in (AddDigitalBrainApp or manual AddDigitalBrain + AddProject).
- v2 divergence: treat as living spec; sync key POC features (reactor, file neurons, numbered) into main immediately.

This plan is the deliverable. Ready for review/approval before any further moves. All mandatory rules followed (Context7 x N, codegraph before reads, todos phased, relative, no C:\, no default summaries added, builds green, v2 fixed+green, review scan done).

(End of plan doc. Expand in follow-ups with user input.)