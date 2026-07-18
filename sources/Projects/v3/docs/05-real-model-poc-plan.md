# Plan: v2 Working POC ASAP with Real Model + Excellent Architecture (Communication + 1.0/2.0 Focus)

**Status**: For user approval before any implementation.  
**Branch**: v2/clean-room-prototype (continue self-contained in `v2/`).  
**Current verified state** (as of this plan):  
- `dotnet build v2/DigitalBrain.V2.slnx` succeeds with 0 warnings (net11.0 preview, LangVersion=preview, Orleans 10.1.1-preview.1 memory streams).  
- `dotnet test v2/DigitalBrain.V2.slnx --no-build` : 5/5 green (Ping, Greeter, Catalog, Ino, Creator simulations).  
- Creator loop works end-to-end via hardcoded `ImplementerNeuron` (always returns fixed `Generated.PingEcho` .ino).  
- All invariants hold: only `Neuron : Grain` + open `Synapse` records; routing via `RoutingMode` metadata on synapse (no Broadcast subtype); wiring via `IHandle<T>`/`IEmit<T>` on `I*Neuron` contracts; tests = `Simulation` (client-side driver today) firing into live silo + asserting timeline.  
- Communication primitives proven: broadcast timeline (memory streams + subscribe filter), point-to-point `Ask<TTarget>(key, synapse)` + `Reply` + `DeliverAsync` (with `AlwaysInterleave` for progress).  
- Catalog + .ino parse/transpile/Roslyn gate + collectible ALC execution proven.  
- C# preview `union` syntax used for `CompileResult`/`GateOutcome` (exhaustive switch, in-process only).  

**Goal of this plan**: Smallest set of changes to reach a **working, demonstrable POC** that:  
- Uses a **real model** (IChatClient via Microsoft.Extensions.AI + OpenAI-compatible provider for xAI Grok or Ollama/OpenAI; falls back to deterministic canned for CI/tests).  
- Maintains **excellent architecture** (strict adherence to "everything is neuron or synapse", communication as first-class via synapses even for LLM step).  
- **Focus on communication**: all flows (catalog, authoring intent, LLM prompt/completion, gate feedback, activation) are explicit `Ask` (p2p) + `Emit` (broadcast) + `Reply`. The closed loop itself becomes the premier demo of v2 communication.  
- Highlights **v1 and v2 software** (Software 1.0 = hand-written C# `Neuron` impl + contracts; Software 2.0 = `.ino` authored by real model, transpiled to equivalent 1.0 C# at runtime; interchangeable peers that communicate via the same synapse contracts and run in the same substrate). No changes outside `v2/`.  

**Non-goals (ruthless delete per Elon's algorithm)**:  
- No durable journaling, no Aspire resources/containers in v2 POC (keep using the existing `Substrate` + memory streams; aspire integration is post-POC).  
- No UI, no marketplace, no federation, no full Ino agent swarm.  
- No changes to parser/transpiler beyond robustness fixes required for LLM output.  
- Do not make `Simulation` derive from `Neuron` yet (deferred per docs/04).  
- No touching v1 code, no new top-level projects if avoidable for speed (prefer adding inside existing or minimal new if arch requires).  

**Key research (Context7 + Microsoft Learn + codegraph + live run, before any code)**:  
- Microsoft.Extensions.AI (v10.6.0 as used in repo; latest compatible): `IChatClient.GetResponseAsync<T>(prompt | messages, options?)` for strongly-typed structured output (auto JSON schema via `ChatResponseFormat`). Returns `ChatResponse<T>`. Use `useJsonSchemaResponseFormat: true`. `ChatOptions`. OpenAI-compatible clients (xAI endpoint works via OpenAI lib). Tool calling and middleware available but not needed for POC.  
- Structured output for code gen: define `record InoDraft(string InoSource, string Notes);`, pass `GetResponseAsync<InoDraft>(...)`. Model is constrained; prompt must still say "return ONLY the JSON, no fences, no prose".  
- Orleans in v2 (matches patterns from /dotnet/orleans): `AddMemoryStreams`, `IAsyncObserver<Synapse>` + `SubscribeAsync`, `GetStreamProvider(...).Timeline().OnNextAsync`, `GrainFactory.GetGrain<T>(key)`, dynamic registration via `GrainTypeOptions` + `TypeManifestOptions.AllowedTypes` for Roslyn-generated neurons (already done in `Substrate` and `GateNeuron` ALC). `AlwaysInterleave` on `DeliverAsync` (in `INeuron.cs` contracts) for p2p reentrancy.  
- C# 11+/preview unions + exhaustive switch: already used successfully in `LoopResults.cs` + `GateNeuron`. Keep for control flow only.  
- Current v2 comms (from codegraph + reads): `Neuron.Fire` (private) stamps via `Synapse.Stamp`, routes broadcast to stream or p2p via `DeliverAsync`. `Incoming` AsyncLocal for causation + `Reply`. Catalog exposes edges for LLM context.  

**High-level POC architecture (communication-centric + 1.0/2.0)**:  
Everything remains neurons/synapses. The "real model" step is just another communicated capability:  

```
CreateNeuron(cap)  [broadcast from test/Sim]
  -> ArchitectNeuron (p2p Ask to Catalog for DescribeConstellation)
       CatalogNeuron (scans contracts -> ConstellationDescribed with ToConstellationText() + edges)
  -> Architect decides -> p2p Ask ImplementerNeuron(ImplementNeuron {cap, diagnostics, attempt, optionalConstellationText?})
       ImplementerNeuron: builds rich prompt (cap + catalog text + prior diagnostics + strict .ino grammar + example + "use only Ping for this POC") 
         -> p2p Ask<ILlmNeuron>("default", LlmPrompt {system, user})
              LlmNeuron (DI IChatClient; real or canned)
                -> real model (GetResponseAsync<InoDraft>) or canned map[cap]
                -> Emit LlmCompletion(inoSource, ...)
       Implementer receives completion -> Reply(NeuronAuthored {cap, inoSource})
  -> Architect -> p2p Ask GateNeuronCandidate
       Gate: parse (InoParser) -> transpile (InoTranspiler) -> Roslyn (InoCompiler) -> collectible ALC -> instantiate generated Simulation -> run its [Fact] (identical path to hand-written CreatorSimulation / InoTranspilerSimulation)
         green -> Emit NeuronActivated
         red (bounded) -> GateFailed -> Architect feeds back to Implementer for retry
  -> test/Sim does Expect<NeuronActivated>
```

- **Communication is the star**: No "service locator" or direct method calls between creator parts outside the grain model. Architect/Implementer/Gate/Llm talk exclusively via `Ask<IFooNeuron>` + handling the response synapse + `Reply`/`Emit`. Catalog is queried the same way. This is the v2 "OS" communication demo.  
- **v1 vs v2 software (1.0/2.0)**: Hand-written capsules (PingNeuron : Neuron, IPingNeuron in C# 1.0) coexist with LLM-generated (Generated.XXXNeuron emitted from .ino 2.0, compiled to 1.0 equivalent at gate time). Both declare identical `IHandle`/`IEmit` shape; both run as grains; both appear in the same `Catalog` graph; a generated neuron can `Ask` a hand-written one (or vice-versa) because contracts are shared. The transpiler ensures the emitted C# is a faithful lowering (matches v1 reference shape but targets v2 primitives). POC will demo mixed activation in one Simulation.  
- **Real model with excellent fallback**: LlmNeuron ctor-injects keyed `IChatClient`. For real: `OpenAIClient` configured for xAI (`https://api.x.ai/v1`, key from env `XAI_API_KEY` or `OPENAI_API_KEY`) or Ollama. Version pinned to repo's 10.6.0. For tests/CI/no-key: a pure in-memory `CannedLlmClient` (no external dep beyond the AI abstration) that returns a known-good .ino variant for the test capability (and can vary slightly on "attempt"). The canned is only in test projects; production neurons can be activated with real client registered at silo level.  
- **Substrate evolution (minimal)**: Extend `Substrate.StartAsync(assemblies, configureHost: Action<IHostBuilder>? = null)` so Creator.Simulations can `services.AddKeyedSingleton<IChatClient>("v2-llm", sp => CreateCannedOrReal(sp))`. Only creator simulation pays the AI package cost. Other simulations unaffected.  
- **Prompting for reliability (ASAP)**: System = "You are a precise .ino author for DigitalBrain v2. Output ONLY a single JSON object matching the schema. inoSource must be valid per the grammar, start with 'neuron FQN', use only provided 'using' aliases, limited scenario syntax exactly as in the Ping.ino example. No markdown, no explanations." User = capability + full catalog.ToConstellationText() + prior diagnostics + "produce a neuron similar to Ping but for <cap>, include one state var and a set, scenario must pass the gate assertions". Use the structured `GetResponseAsync<InoDraft>`.  
- **Canned for green tests always**: Even with real model available, the default CreatorSimulation uses a capability whose canned response guarantees identical behavior to current hardcoded (so test remains fast/deterministic/no net call unless opted in). A separate manual "real model demo" run (or env flag) can exercise a different cap that forces LLM path.  
- **Unions & naming**: Continue using `union` preview syntax (already in LoopResults). Self-explanatory names only; no `/// <summary>` ever. Inline comments only in exceptional cases.  

**Implementation slices (strict order; each must `dotnet build v2/DigitalBrain.V2.slnx` 0 warnings + `dotnet test ...` all green before next commit)**:  

**Slice 1: Substrate + DI hook for AI clients (no AI package yet)**  
- Modify `DigitalBrain.V2.Testing/Substrate.cs`: overload/optional `configure: Action<IHostBuilder> configureApp = null`. Apply after UseOrleans, before Build. Also expose a way for parts + services.  
- Update all callers (existing sims) — they pass null, behavior identical.  
- Update CreatorSimulation.Prime to pass a configure that registers a placeholder (still no package).  
- Add/update contracts if needed for "Llm client key".  
- Build + test all (Creator still uses hardcoded path). Green. Commit.  

**Slice 2: Add real-model packages + Llm contracts + canned impl (communication via new synapses)**  
- Update `DigitalBrain.V2.Creator/DigitalBrain.V2.Creator.csproj` (and its .Simulations) to reference latest (10.6.0):  
  `Microsoft.Extensions.AI`  
  `Microsoft.Extensions.AI.OpenAI` (for real provider; optional in some runs).  
- In `DigitalBrain.V2.Creator` (or minimal new `DigitalBrain.V2.Ai` if project overhead acceptable for arch — decide for speed: start inside Creator.Contracts for POC velocity):  
  - `LlmContracts.cs`: `ILlmNeuron`, `LlmPrompt(string SystemPrompt, string UserPrompt, int Attempt) : Synapse`, `LlmCompletion(string InoSource, string[] Diagnostics) : Synapse`.  
  - Simple `CannedLlmClient : IChatClient` (implements only what's needed for GetResponseAsync<InoDraft>; returns fixed JSON for known caps like "Generated.PingEcho", "Generated.EchoWithState"; can key off capability embedded in prompt).  
- Update `CreatorContracts.cs` + `ImplementerNeuron` interface to carry optional catalog snapshot text or rely on Architect to enrich `ImplementNeuron` with it (prefer passing via synapse for comms purity).  
- ImplementerNeuron now (still hardcoded for this slice? or partial): on Implement, Ask<ILlmNeuron> the prompt (build prompt helper that includes catalog.ToConstellationText() when available). Handle LlmCompletion by replying NeuronAuthored( from completion.InoSource ).  
- LlmNeuron (in same assembly for speed): `LlmNeuron(IChatClient client) : Neuron, ILlmNeuron`. In Handle: construct messages, `var draft = (await client.GetResponseAsync<InoDraft>(...)).Result; Emit(new LlmCompletion(draft.InoSource, []));`.  
- For now, the "client" registered in configure is always the Canned (real wiring in slice 3).  
- Keep existing hardcoded path temporarily or replace Implementer body; ensure CreatorSimulation still passes exactly (use same cap + canned returns byte-for-byte what hardcoded did).  
- Enhance Architect slightly if needed to include constellation text in the Ask to Implementer (via extended synapse fields).  
- Build + full test green. (InoTranspilerSimulation and Creator use same .ino shape.) Commit.  

**Slice 3: Wire real model + opt-in + demo non-trivial generated neuron**  
- In Creator.Simulations (or a new helper), implement `CreateChatClientForV2Poc()`:  
  - If env var `XAI_API_KEY` (or `OPENAI_API_KEY`) present: `new OpenAIClient( new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new("https://api.x.ai/v1") } ).GetChatClient("grok-3-mini" or "grok-2-latest").AsIChatClient()`.  
  - Else: the CannedLlmClient.  
  - Register as keyed "v2-llm" (or whatever).  
- Update LlmNeuron to use keyed lookup from activation services (consistent with how Logger is resolved today).  
- Add a second test fact or new cap in CreatorSimulation: `CreateNeuron("Generated.EchoWithState")` (or keep one test, make canned support "realistic" variation that still passes). Ensure the .ino from canned/LLM includes `state lastSeen: text`, `set`, `emit`, and scenario "when/then with field". Assert activation.  
- Update prompt builder (pure string helper) to be strict + include full example from Ping.ino + "Your output .ino will be parsed by a simple regex+indent parser and must transpile/run without error."  
- Optional: make Architect pass richer context (edges text) on the ImplementNeuron synapse.  
- Manual verification note: `XAI_API_KEY=... dotnet test ...` should hit real path and still pass (logs the call).  
- Build + test (default no-key path) green. Commit.  

**Slice 4: Mixed 1.0/2.0 communication demo + polish + docs**  
- In CreatorSimulation or a lightweight new "InteropSimulation" (still in Creator.Simulations): after activation of generated, also Activate a hand-written (e.g. Greeter or Ping), Fire a synapse from generated context if the authored .ino asks something, or simply fire a broadcast that both a 1.0 and 2.0 neuron handle, assert both see it (via catalog or timeline count). Or simpler: have the generated .ino for a cap that does `ask greeter to ...` or just prove via catalog that generated appears alongside hand-written in ConstellationDescribed.  
- Assert in test that catalog (re-queried) or activation proves interchange: the generated neuron contract shape is identical.  
- Harden: if LLM/canned produces extra whitespace or minor var, make parser/transpiler tolerant where safe (no behavior change).  
- In Gate/Implementer, improve diagnostics on failure to include the exact .ino source snippet (for retry prompt).  
- Update `v2/docs/04-minimum-and-roadmap.md` (move "real model + LlmNeuron via comms" and "1.0/2.0 mixed demo" from deferred to done).  
- Update `v2-clean-room-prototype.md` with the new slice results + "real model POC achieved; communication is the spine; 1.0/2.0 are peers".  
- Run code review (per rules): self-review for no `/// <summary>`, good names (e.g. no "FooService"), invariants, only essential comments, latest pkgs, Context7 compliance in the added AI usage.  
- `dotnet build` + `dotnet test` green. Commit slice.  

**Post-POC / follow-ups (after approval + this lands)**:  
- Full in-silo Simulation as Neuron (IDigitalBrain inside).  
- Better prompt iteration / few-shot from real catalog entries.  
- Wire LlmNeuron to use v1-style bundles when v2 is hosted inside main aspire app (future interop slice, still no touch outside v2 for now).  
- Add "real model" integration test that requires key (skipped otherwise).  
- Consider tiny `DigitalBrain.V2.Ai` project split for purity.  
- Then aspire host for a v2-only brain (new AppHost slice).  

**Risks & mitigations (ASAP focus)**:  
- Flaky LLM output breaks gate: canned default + structured JSON + ultra-strict prompt + bounded retries + diagnostics fed back. Test always green.  
- Adding packages to Creator only: yes (simulations that need it pull it).  
- Parser too brittle for real LLM: slice 2/3 include prompt constraints + any minimal parser tweak (with test).  
- Orleans dynamic load for generated + AI services: already works for generated; DI for IChatClient follows existing Logger pattern + configure hook.  
- net11 preview + union + Orleans preview: already proven; stick to same.  

**Definition of POC done (user can run & see)**:  
- `dotnet test v2/DigitalBrain.V2.slnx` still 100% green (default canned path).  
- With `XAI_API_KEY=...` (or equivalent) the Creator loop exercises real model path and still activates (or a dedicated demo console/app if added minimally).  
- A generated-from-real-LLM neuron (or canned equivalent) coexists in the same simulation substrate with hand-written Ping/Greeter; catalog shows edges for both; communication (Ask/Emit across them or within loop) works.  
- No violations of 00-04 invariants or CLAUDE rules.  
- Updated docs + memory file.  

**Execution rules (must follow on every slice)**:  
- Use latest nuget (10.6.0 for the AI pkgs).  
- Context7 verified before any new API surface in the changes (already done in planning for AI + Orleans).  
- `dotnet build v2/DigitalBrain.V2.slnx` (0 warnings) + `dotnet test` after every edit.  
- No `/// <summary>`. Prefer self-documenting names. Rare inline comments.  
- Each slice own commit. Code review before "return".  
- Stay in `v2/`.  

**Next action after approval**: Implement Slice 1 (smallest, no new pkgs), verify green, then 2 etc. Use todo tracking. Run full verification including manual real-key path if key available locally.  

This plan delivers a **working real-model POC fast** while the architecture (synapse-centric communication for *everything*, including the model, + explicit 1.0/2.0 peer interop via contracts + catalog) is excellent and true to the manifesto. Ready for your review/approval or refinements via questions.