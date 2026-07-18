# DigitalBrain — Unification Plan (ino.cs, bundles-as-resources, telemetry, LLM neurons)

Status: brainstorm + plan, produced 2026-06-12 from full assessment of `E:\Projects\final` @ HEAD `cc6f4b1` ("Remaining complete (mTLS + Flutter expand + Global basic)").
Process: The 5 Steps, in order. Steps 1–2 are the contract of this document; Steps 3–5 are staged work (U0–U5) that must not start before the Step-1 decisions below are answered by the owner.
Owner of every requirement: Vlad (stated 2026-06-12, this session). Each requirement is questioned back anyway — that's Step 1.

---

## 0. Current state (what actually exists at HEAD)

- **Two parallel topologies.** `start.cs` (~200 lines: port picking ×3 local functions, env juggling, first-run username, beacon guidance, flutter best-effort spawn via `Task.Run`, peer auto-connect, keyed gemma `IChatClient`) and `src/DigitalBrain.AppHost/AppHost.cs` (dashboard token construction, session/port-offset hackery, Ollama+Redis resources, two `AddDigitalBrainDomain(...).AddKernel(...)` kernels, flutter as `AddExecutable`). They drift; both own ports, env, LLM registration, and flutter startup in different ways.
- **Silo wiring is already half-unified.** `UseDigitalBrain(DigitalBrainSiloOptions)` / `UseDigitalBrainClient` in `DigitalBrain.Aspire.Hosting/Extensions.cs` is the Phase-1 win — keep building on it.
- **Bundles exist at three maturity levels in the tree:** full experiences (`ActivateExperiencesFor` name-prefix activation of compiled-in neurons), contract-only bundles (`IsContractOnly`, 17/17 green, `CONTINUATION-PRIVATE-MARKETPLACE-CONTRACTS.md` completed @ `2a7381d`), and rule capsules (`HasRules` → `RuleHostNeuron` + `InoParser`/`RuleInterpreter` in `Core/Domain/Ino`, BDD green) — **despite** `INOLANG-RFC.md` recording B as demoted at Gate 0 (12/20 = 60% < 80%). This contradiction must be resolved (D2 below).
- **Telemetry is split-brain.** `NeuronTelemetry` synapse on the timeline (journal = truth) vs. Aspire `ConfigureOpenTelemetry` (Orleans meters/sources only). Neuron Receive/Emit produce no spans/metrics; LLM calls are not traced; the dashboard cannot show "what did this neuron do."
- **LLM access is ad-hoc.** Keyed `IChatClient` registrations duplicated in `start.cs` and `Kernel/Program.cs`; `LlmAgentNeuron` resolves from `_services`, builds tools by hand, carries two giant inline ino system prompts. `WithLLM` on `DigitalBrainDomainResource` is a no-op (delegates to `WithModels`, comment admits it).
- **Flutter** is both an Aspire `AddExecutable` resource (AppHost) and a best-effort `Process.Start` (Flutter.cs + start.cs `Task.Run`) — two owners of one process.
- **IAspire neuron** already does `ResourceCommandService.ExecuteCommandAsync(Restart/Start)` and `RestartDomainKernelAsync` via `AssociatedKernelResourceName` — the restart machinery for bundle silos is 80% present.
- The **ino/ repo** (`E:\Projects\ino\src\Ino.AppHost\Program.cs`) is the best prior art for the fluent surface: `AddIno("ino").WithLlm<Grok4FastNonReasoning>().AsFast()...AsBalanced()...AsReasoning().WithVoiceToText<...>()`, per-domain silo project resources, `PropagateInoConfig`.
- **External fact verified 2026-06-12:** Aspire 13 supports single-file AppHosts (`apphost.cs` + `#:sdk Aspire.AppHost.Sdk@13.x`, no csproj; `aspire run` and `aspire add --file` work; not supported in Visual Studio — VS Code / CLI only). This is the mechanism that makes `ino.cs` the real front door instead of a third topology.

---

## 1. Step 1 — Make the requirements less dumb (each questioned, owner: Vlad)

| # | Requirement as stated | Challenge | Verdict |
|---|---|---|---|
| R1 | "`dotnet run ino.cs` starts everything and wires everything up, gracefully" | Today this would be a **third** topology next to start.cs and AppHost. A new entry point is only acceptable if it *replaces* both. Also: `dotnet run ino.cs` on a single-file AppHost works, but the blessed verb is `aspire run` (single-file AppHosts resolve DCP/dashboard via the Aspire CLI bundle; plain `dotnet run` can hit ASPIRE009 without the CLI present). | **Keep, sharpened.** `ino.cs` = single-file Aspire AppHost at repo root using `builder.AddDigitalBrain(...)`. It replaces `AppHost.cs` content (project stays as a thin shim or dies) and absorbs start.cs topology. Fate of start.cs = D1 (measured, not assumed). |
| R2 | `.WithUI(flutter => flutter.WithWindows(autostart: true))` | Autostart already "works" twice (AddExecutable resource + best-effort spawn). Two owners is the dumb part, not the requirement. | **Keep.** UI is a resource; the resource is the only process owner. `IFlutter` neuron keeps driving it *through* `ResourceCommandService` only. Delete both spawn paths. |
| R3 | "`kernel.WithBundle<Marketplace>` or marketplace as separate resource and solo? unification here" | Wrong question shape: it's framed as architecture but it's **deployment**. The marketplace's identity is its contract (`IMarketplace` + distribution synapses), not its process. | **Unify as: bundle is the unit; hosting is a one-line decision.** `WithBundle<T>()` = in-proc activation in the kernel silo (today's behavior, default). `.AsSilo()` = same bundle promoted to its own Orleans silo in the same cluster as a separate Aspire project resource. Orleans heterogeneous-cluster placement puts the bundle's grains on the only silo carrying the implementation; every other silo compiles against the contract package only. **Do not build `.AsSilo()` now** — design the API so promotion is one line, implement on first real need (Step-2 discipline). |
| R4 | "kernel actually might be shipping in marketplace itself in form of .ino files; make it the goal" | Two dumb parts. (a) **Bootstrap paradox**: something must exist to install the marketplace — the installer can't be only installable. (b) **Post-Gate-0 honesty**: `.ino` text cannot carry kernel-grade behavior; B scored 60% author-reality and even before that, rules were deliberately IO-free and non-Turing-complete. Behavior mobility is D (source, L2-gated) or L3 (compiled silo bundle). | **Reshape to microkernelization.** Kernel shrinks to substrate: boot, cluster, identity (keys), timeline, `DigitalBrainGrain`, `RuleHost` (if D2 keeps it), `Aspire`/`Flutter` bridge neurons. *Every other experience* (Marketplace, Packager, Creator, LlmAgent, Memory, WeatherWatcher, Transcription, HexGuide, KernelTaskSupervisor) becomes a bundle with a capsule under `pa-files/marketplace`. The marketplace itself is a bundle with one preinstalled copy seeded at boot — exactly how apt ships as a .deb but is preinstalled. "Kernel ships in marketplace" becomes "kernel **experiences** ship in marketplace": achievable, honest, and it forces the contract/implementation split R3 needs. |
| R5 | "go bull on Aspire resources and the Aspire way of working for silos/resources" | Bull where? If "everything is an Aspire resource" includes message flow, it breaks Core Law 1 (no side channels). | **Accept with one boundary.** Aspire owns *topology*: processes, models, storage, tunnels, UI, restart/upgrade lifecycle, dashboards, typed commands. Orleans owns *behavior*: neurons + synapses. A resource is never a transport for synapses; `IAspire`/`IFlutter` neurons remain the only bridge (they already model this correctly). |
| R6 | "private software, like marketplace with public contract — install software and restart silo with new version after rebuild" | This collides head-on with Core Law 3: "Install grows the brain, **never restarts it**." Don't quietly violate a Core Law — amend it explicitly. | **Amend Core Law 3:** *Install grows the brain and never restarts it (L0/L1 — proven N+1 today). Upgrading an L3 silo bundle restarts that bundle's silo only; the brain, the kernel silo, and the UI keep running.* Orleans cluster membership handles departure/rejoin; in-flight grain calls to the departing silo retry per normal Orleans semantics. New synapses: `UpgradeBundle(id, version)` → `BundleUpgraded`. The mechanics already exist: `Aspire.RestartResourceAsync` + `AssociatedKernelResourceName` pattern extended to bundle-silo resources. The private/public split: contract package public (NuGet-shaped, like `Ino.Domains.*` in ino/), implementation private to the silo bundle — the `IsContractOnly` machinery is the proof this split serializes and counts correctly. |
| R7 | "full dynamic neurons and synapses" | "Fully dynamic" at runtime inside the root silo means runtime `Assembly.Load` of foreign code — the lineage buried that twice (v1 interpreter decay; ino/'s code-in-synapse named as the trust anti-pattern). Orleans codecs for *new synapse types* require compiled `[GenerateSerializer]` types on every silo that touches them. | **Decompose into the four dynamism levels that already exist, and name them:** **L0** contract-only (shape, N+1 counting — done), **L1** descriptor + rule capsule (behavior over the *existing* vocabulary, interpreted, IO-free — landed, status pending D2), **L2** source codegen compiled in the quarantine world behind Trust gates (ROADMAP Phase 4, v2 transpiler lowering as the asset), **L3** silo bundle (new types + new neurons ship *compiled* in their own silo resource; contract package distributed to consumers that need to speak the new types). "Fully dynamic" = L3 for new types, L1 for new behavior over old types. No fifth mechanism. |
| R8 | "base DurableNeuron with built-in Telemetry static class — own logs/metrics/traces per neuron, OTel under the hood, Aspire OTel config" | "Static class per neuron" is an OTel anti-pattern (source/meter cardinality explosion). Also: the journal already *is* per-neuron telemetry — don't build a second truth. | **Accept, shaped:** ONE static holder (`NeuronTelemetry` static class: one `ActivitySource("DigitalBrain.Neuron")`, one `Meter("DigitalBrain.Neuron")`); per-neuron identity travels as *tags* (`neuron.type`, `neuron.key`, `db.world`). The Neuron base instruments Receive/Emit so every neuron gets traces+metrics for free. The journal stays the truth; OTel is a projection of the same events to the Aspire dashboard. One call site emits both. See §3.3. |
| R9 | "each LLM neuron and agent should have built-in IChatClient; for agent, the IChatClient would also have tools" | Agreed — but "built-in" must mean resolved-from-keyed-DI through a tier registry, not constructed per neuron (model endpoints are topology, owned by Aspire/R5). And tools should not be a hand-maintained list. | **Accept:** `LlmNeuron : Neuron` exposes `protected IChatClient Chat` (tier-resolved, OTel-instrumented pipeline). `AgentNeuron : LlmNeuron` adds `.UseFunctionInvocation()` + a tool registry. Unification idea worth a spike: **emits-as-tools** — every `IEmit<T>` the agent declares auto-projects to an `AIFunction emit_T(...)` built from the synapse constructor (sourcegen already catalogs contracts in `KnownContracts`). The contract becomes the tool list. See §3.4. |
| R10 | LLM tiers like ino/: `.WithLlm<Grok4FastNonReasoning>().AsFast()...` | Nothing dumb — it's the proven pattern from ino/ and IAW. The dumb part is final/'s current no-op `WithLLM`. | **Adopt:** `.WithLlm<TModel>().AsFast()/.AsBalanced()/.AsReasoning()` on the DigitalBrain resource; tier names (`fast`/`balanced`/`reasoning`) are the keyed-DI keys; `AgentRequest.PreferredModel` maps to tier or explicit key. Delete the no-op. |
| R11 | Flutter goal: ino code editor + Validate + Run + demo; preinstalled experience that OAuths into Gmail and writes last 10 senders to a file; "code colored with experiences" | Three buried sub-requirements that must be separated: (a) editor+validate+run, (b) the Google auth bundle, (c) capability policy collision — `SaveFileRequest` is on the Q2 privileged deny list and the demo *requires* it. (c) silently violating Q2 would be the dumb path. | **Accept as the flagship #2 demo** ("the brain reads my Gmail because I tapped Allow"), with (c) escalated to D3: the demo forces install-time capability grants (a surface: "gmail-last-senders wants: SaveFileRequest, GoogleApi — Allow / Deny", the answer journaled as a synapse). The OAuth shape is sound *because the kernel runs on the user's machine*: loopback redirect URI on the kernel's Kestrel, surface carries a `Hyperlink`/open-URL widget (or a Button whose handler asks Flutter to launch the URL), token lands in a brain-scoped encrypted secret grain. Port the three v2 scenarios from `E:\Projects\v2\DigitalBrain.Auth.Google\Testing\AuthGoogle.ino` verbatim as the BDD spec: per-brain token isolation, encryption at rest, connector-reads-secret-store. "Colored with experiences" = editor token coloring driven by `ValidateIno` + the known-synapse table: each trigger/emit colored by owning bundle, INO002 unknowns red, INO004 privileged amber. |
| R12 | "form the role of ino as AI personal assistant on DigitalBrain" | The word "ino" currently means four things: the language (`.ino`), the assistant persona (inline prompts in LlmAgentNeuron), the sibling repo (`E:\Projects\ino`), and now the entry file. That ambiguity is the dumb part. | **Name discipline:** **ino** = the assistant (the personality, one identity, one system-prompt source built from live contracts — not two divergent inline strings); **`.ino`** = skills/experiences ino learns and trades; **`ino.cs`** = the door you knock on to wake the brain; **InoLang** = the (frozen-pending-D2) rule grammar. The sibling repo keeps its own name; nothing in final/ references it at runtime. |

### Decisions Vlad must make before Step 3 work starts

- **D1 — start.cs fate.** Measure `aspire run ino.cs` warm start vs `dotnet run start.cs`. If warm topology-up ≤ ~10s, delete start.cs entirely (TUI becomes `dotnet run --project src/DigitalBrain.Clients.Console` against the running cluster, or an `aspire`-launched resource). If >10s, start.cs survives *only* as a ~30-line solo shim: `UseDigitalBrain(opts)` + `TaskManagerClient.RunAsync` — zero topology logic of its own.
  U0 measurement (2026-06-12, .NET 11p4 + aspire 13.5p CLI): single-file probe (`#:sdk Aspire.AppHost.Sdk@13.4.3` + `#:project src/DigitalBrain.Sdk/...`) under `aspire run --file apphost-probe.cs --non-interactive` hit dashboard start in 3.1s (key log found). Current AppHost project aspire run ~4.6s to dashboard. start.cs measure hit first-run input (30s cap); env-prep yields <5s to setup. Aspire single-file form + project-ref Sdk compiles and hosts successfully on this SDK combo. Decision (per <=~10s): delete start.cs. TUI connects via project client or aspire resource. Probe deleted post-record. (See §6 table note.)
- **D2 — InoLang-B status.** The RFC demoted B (Gate 0: 60%), yet `RuleHostNeuron` + parser + interpreter + BDD landed afterward and are green. Pick one: (a) re-run Gate 1 with live Ollama/gemma (the Gate-0 run used a deterministic stub — its 60% is simulated, not measured) and re-authorize B if ≥80%; or (b) keep the landed machinery frozen (no grammar growth, demo "Run" may use it for trigger registration) and route all new behavior through L2/L3. Default if unanswered: (b) freeze.
- **D3 — capability grants.** Q2 said "no install-time user override in v0". The Gmail demo requires `SaveFileRequest` (+ a new `GoogleApi`-ish capability). Either amend Q2 to allow the journaled Allow/Deny surface, or the demo writes through a non-privileged sink (e.g. emits a `FileSaved` surface and the *client* saves). Recommended: amend Q2 — the grant UX is itself a product feature.
- **D4 — first `.AsSilo()` promotion trigger.** Candidates: the Google-auth bundle (secrets isolation is a genuinely good reason for its own silo) or Marketplace (the original ask). Recommended: GoogleAuth first — smaller contract, real isolation motive, and it exercises the private-impl/public-contract split end to end.
- **D5 — verb.** Bless `aspire run` (README headline) with `dotnet run ino.cs` documented as the equivalent; or invest in making `dotnet run ino.cs` the headline (requires the Aspire CLI bundle present anyway). Recommended: `aspire run` headline, ino.cs filename keeps the brand.

---

## 2. Step 2 — Delete (with owners; goes to DELETED.md at execution)

1. **start.cs topology logic** — `PickFreePorts`/`PickKestrelPort`/`Sanitize`, env juggling, dashboard URL fabrication, peer auto-connect block, flutter `Task.Run` spawn, keyed gemma registration. Absorbed by ino.cs + `AddDigitalBrain` + `AddDigitalBrainLlms`. (Owner: Vlad, R1/D1.)
2. **Flutter best-effort `Process.Start` branch** in `Sdk/Microsoft/Flutter/Flutter.cs` and the start.cs spawn. One owner: the Aspire executable resource; `IFlutter` drives it via `ResourceCommandService` only. The "standalone dev" case is covered because ino.cs *is* standalone now. (R2.)
3. **No-op `WithLLM`** on `DigitalBrainDomainResource` — replaced by the real tiered `WithLlm<TModel>()`. (R10.)
4. **Duplicate session/portOffset computation** — exists in both `AppHost.cs` and `DigitalBrainDomainResource.AddKernel`. One owner inside `AddDigitalBrain`. 
5. **Duplicate keyed `IChatClient` registrations** (start.cs + Kernel Program) → one `AddDigitalBrainLlms(IHostApplicationBuilder)` extension reading tier env/conn-strings that `WithLlm` injects.
6. **UDP beacon inline `Task.Run` in `Kernel/Program.cs`** — Program is boot-only; the beacon moves into MarketplaceNeuron activation (it already owns `/market scan` peers) or a tiny DiscoveryNeuron if Marketplace bloats. No new behavior, just an owner.
7. **The two inline ino system prompts** in `LlmAgentNeuron` → one persona source composed from live state (active bundles, tabs, peer addr) instead of hardcoded narrative. (R12.)
8. **`AppHost.cs` dashboard-token fabrication** — re-evaluate against Aspire 13.4 (`aspire run` owns token printing); keep only if the copy-whole-URL UX measurably needs it, else delete ~25 lines.
9. **`(dynamic)` activation + `grainClassNamePrefix` magic** (already ROADMAP Phase 1 item 6 — pulled into U1 here because microkernelization touches the same code).
10. **Candidate, pending D2(b):** freeze means deleting the *plans* for grammar growth (ValidateIno categories stay; no new statement kinds), not the landed code.

If we're not adding ~10% of this back later, we didn't delete enough — the most likely re-add is a thin start.cs shim if D1 measurement says aspire startup is too slow for the inner loop.

---

## 3. Step 3 — Simplify / unify (only what survived 1–2)

### 3.1 `ino.cs` — the one front door (single-file Aspire AppHost)

Target shape (names final, mechanics per Aspire 13.4 single-file AppHost; `#:project` directives reference the hosting lib):

```csharp
#:sdk Aspire.AppHost.Sdk@13.4.3
#:project src/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj

var builder = DistributedApplication.CreateBuilder(args);

var vlad = builder.AddDigitalBrain("vlad")                 // composite: kernel project resource + wiring
    .WithLlm<Gemma3>().AsFast()                            // tier registry -> keyed IChatClient "fast"
    .WithLlm<Nemotron3Nano>().AsReasoning()
    .WithVoiceToText<WhisperLocal>()
    .WithDurability(d => d.Redis())                        // root-only Redis Default storage, as today
    .WithBundle<Marketplace>()                             // in-proc (default); .AsSilo() = promotion, one line
    .WithBundle<Creator>()
    .WithBundle<GoogleAuth>()                              // D4 candidate for first .AsSilo()
    .WithBundle<GmailLastSenders>()                        // preinstalled demo experience
    .WithUI(ui => ui.Flutter(f => f.Windows(autostart: true)))
    .WithPeerDiscovery();                                  // beacon ownership moves here (Step-2 #6)

builder.AddDigitalBrain("example-world").WithLlm<Gemma3>().AsFast();

builder.Build().Run();
```

Implementation notes:
- `AddDigitalBrain` is a refactor of `AddDigitalBrainDomain` + `AddKernel` (they already exist) — composite resource, single owner of ports/session/cluster-id, child resources for models/redis/flutter with `WithReference` propagation (the ino/ `PropagateInoConfig` pattern).
- `WithUI(...Flutter(autostart:true))` = today's `AddExecutable("flutter-client", "flutter", ..., "run", "-d", "windows")` + env wiring, moved inside the hosting lib; `autostart:false` = `WithExplicitStart()`.
- Kernel side stays `UseDigitalBrain(options)` — unchanged contract, options grow `Tiers` and `Bundles`.
- Demo-mode reality check: `start.cs`'s value was containerless speed. ino.cs keeps that by making heavy resources optional: `--no-ollama` style profiles or `WithLlm<Gemma3>().AsFast().Optional()` degrade to `DefaultSetup.UseDemoMode`. Measure first (D1).

### 3.2 Bundle model — one ladder, four rungs (R3/R4/R6/R7 unified)

| Level | Carries | Install effect | Restart cost | Exists today |
|---|---|---|---|---|
| **L0 contract** | synapse shapes + handler declarations (`IsContractOnly`) | N+1 counting, vocabulary | none | ✅ green |
| **L1 experience** | descriptor `.ino` (+ rule section pending D2) | activation / rule registration, surfaces | none | ✅ green |
| **L2 source** | generated C# source, sim-gated | compiled in quarantine world, promoted on green | none (quarantine absorbs it) | ROADMAP Phase 4 |
| **L3 silo bundle** | compiled implementation assembly + public contract package | new Aspire project resource joins the cluster; grains place on it (Orleans heterogeneous placement) | **its own silo only** (amended Core Law 3) | API designed now, built on D4 trigger |
3
- Marketplace lists all four with a level badge; `/install` resolves level → install path. `UpgradeBundle(id, version)` → for L3: write package, `Aspire.RestartResourceAsync(bundleResourceName)`, emit `BundleUpgraded` when the silo rejoins (cluster membership event observable through Orleans).
- Microkernelization (R4) is *re-leveling existing kernel experiences*, mostly to L1 capsules over compiled-in neurons at first (zero behavior change, capsule + manifest become the unit of identity/versioning), then selectively to L3.
- The `awesome-se-team` seeding pattern (Aspire `AddStartupTask`, Interlocked-guarded, single owner) generalizes: preinstalled = seeded capsules under `pa-files/marketplace`, including the marketplace's own capsule.

### 3.3 Telemetry — journal is truth, OTel is the projection (R8)

```csharp
public static class NeuronTelemetry   // Core, static, ONE source/meter for all neurons
{
    public static readonly ActivitySource Source = new("DigitalBrain.Neuron");
    public static readonly Meter Meter = new("DigitalBrain.Neuron");
    public static readonly Counter<long> SynapsesIn  = Meter.CreateCounter<long>("db.synapses.in");
    public static readonly Counter<long> SynapsesOut = Meter.CreateCounter<long>("db.synapses.out");
    public static readonly Histogram<double> HandleMs = Meter.CreateHistogram<double>("db.handle.duration");
}
```

- `Neuron.Receive` wraps dispatch in `Source.StartActivity($"{synapseType} → {neuronType}")` with tags `neuron.type`, `neuron.key`, `db.world`, `synapse.type`, `db.correlation`, `db.causation` (lineage as span attributes → the Aspire dashboard trace tree mirrors the synapse causality chain; the existing `AddActivityPropagation()` carries context across grain calls).
- `Neuron.Emit/Fire` → `SynapsesOut` + activity event.
- The existing `Telemetry(...)`-style emissions collapse to one protected method: `Telemetry(string @event, Dictionary<string,string> data)` → emits the `NeuronTelemetry` synapse **and** adds an activity event + counter. One call site, two sinks; never two calls.
- `ConfigureOpenTelemetry` in Extensions.cs adds `.AddSource("DigitalBrain.Neuron")` + `.AddMeter("DigitalBrain.Neuron")` — that's the whole Aspire wiring; the dashboard picks it up with zero further config (R5: Aspire owns export, neurons own emission).
- Naming honesty: the type name `NeuronTelemetry` is currently the *synapse*. Either the static class takes another name (`NeuronInstrumentation`) or the synapse is renamed — do not overload.

### 3.4 LLM neurons — `LlmNeuron` / `AgentNeuron` bases (R9/R10)

```csharp
public abstract class LlmNeuron : Neuron
{
    protected IChatClient Chat => field ??= BuildChat();   // tier-resolved, cached per activation
    protected virtual string DefaultTier => "fast";
    private IChatClient BuildChat() =>
        new ChatClientBuilder(ResolveKeyed(PreferredTierOrDefault))
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")   // LLM spans join neuron traces
            .Build();
}

public abstract class AgentNeuron : LlmNeuron
{
    protected override IChatClient BuildChat() => base pipeline + .UseFunctionInvocation();
    protected virtual IEnumerable<AITool> Tools { get; }   // default: emits-as-tools projection
}
```

- Tier registry: `WithLlm<TModel>().AsFast()` injects `DIGITALBRAIN_LLM_FAST=<conn/model>`; `AddDigitalBrainLlms` (silo side) registers keyed `IChatClient`s `"fast"/"balanced"/"reasoning"` plus model-name aliases. `AgentRequest.PreferredModel` resolves alias → tier → default.
- **Emits-as-tools spike:** sourcegen already catalogs `(NeuronInterface, SynapseType, IsHandle)` in `KnownContracts`. For an `AgentNeuron`, project each `IsHandle=false` declaration into `AIFunctionFactory.Create`-style tools whose invocation constructs the synapse and `Emit`s it — the agent's tool list *is* its contract, no hand-maintained registry, and tool calls are journaled like everything else (they are emissions). Hand-written tools remain possible for read-only queries (`get_brain_recent_history` etc.). Gate the spike on local-model function-calling reality (the same honesty Gate 0 enforced).
- `LlmAgentNeuron` becomes the first `AgentNeuron` subclass; its two inline prompts merge into one persona builder (R12).

### 3.5 Flagship demo #2 — "ino reads my Gmail" (R11)

Slice order (each independently shippable):
1. **Creator/editor parity in Flutter**: editor widget + Validate button wiring `ValidateIno` (exists in `Core/Domain/Ino/InoValidator.cs`) → diagnostics surface; Run = pack → install into the quarantine world → trigger → surface back. TUI already has the Creator tab; this is the Flutter leg over the same synapses.
2. **`google-auth` bundle (L1 now, D4 candidate for first L3)**: brain-scoped encrypted secret grain; OAuth loopback endpoint on the kernel Kestrel (kernel runs on the user's machine — `http://127.0.0.1:{port}/oauth/callback` is a valid Google redirect URI for desktop apps); flow: surface Button "Connect Google" → `BeginGoogleAuth` → neuron emits `AuthLinkReady(url)` surface with a Hyperlink/open-URL widget → user authenticates → callback hits Kestrel → token encrypted into the secret grain → `GoogleAuthCompleted`. Port the three `AuthGoogle.ino` scenarios from v2 as the BDD spec (isolation per brain key, encryption at rest, connector-reads-secret-store).
3. **`gmail-last-senders` experience (preinstalled)**: trigger → GmailNeuron (Google API via stored token) lists 10 messages → capability-gated `SaveFileRequest` (D3) → result surface with the file path + senders table.
4. **Experience-colored editor**: token classification from ValidateIno + known-synapse table → color by owning bundle; unknown = red (INO002), privileged = amber (INO004). Pure client feature; no union change (coloring metadata rides the existing diagnostics shape).

UiWidget union impact (the expensive move, per TUI-redesign discipline): likely one addition — `Hyperlink(string Text, string Url)` (or reuse Button + a client-side `OpenUrl` ClientAction). Decide in U4; round-trip test mandatory either way.

---

## 4. Step 4 — Accelerate cycle time (only after 1–3 land)

- One topology = one watch loop: `aspire run` ino.cs; bundle silos (once any L3 exists) rebuild + restart independently — the upgrade path *is* the dev loop (`UpgradeBundle` in dev = rebuild + restart resource).
- Inner loop stays: `run-ci.ps1` + the high-sev `DistributionDynamicHandlers` gate <60s; `aspire do build` smoke after hosting changes (both already ritual).
- D1 measurement decides whether a containerless fast profile (`--solo` / `.Optional()` resources / demo mode) is needed; do not pre-build it.
- Aspire 13 `aspire do` pipelines: encode build → high-sev gate → AppHost build → smoke as one pipeline so the ritual is one command.

## 5. Step 5 — Automate (last)

- **Test topology = prod topology**: the existing `Aspire.Hosting.Testing` two-kernel E2E migrates to `AddDigitalBrain` the moment it exists — any drift between test and ino.cs becomes a compile error, not a discovery.
- `publish-experience` typed command on the kernel resource gets its real implementation (today it only tags the Activity); add `upgrade-bundle` typed command (L3) → both visible in dashboard + `aspire resource`.
- CI packs the re-leveled kernel-experience capsules (R4) on every green build so `pa-files/marketplace` is always installable at HEAD.
- Headless TUI scenario tests + Flutter widget tests extend to the editor/validate/run loop and (with a fake Google endpoint) the auth flow.

---

## 6. Execution stages

| Stage | Delta | Gate |
|---|---|---|
| **U0** | Answer D1–D5; measure aspire warm start; record answers in this doc. Measured: probe 3.1s dashboard (delete start.cs); aspire 13.5p + 13.4.3 Sdk form works. Commit 3ca0d56 | — (gate run post-U0: DistributionDynamicHandlers 0 fail) |
| **U1** | `AddDigitalBrain` composite (refactor of AddDigitalBrainDomain/AddKernel) + `AddDigitalBrainLlms` + tiered `WithLlm` + deletions 3/4/5/8/9 | high-sev 0 fail; `aspire do build`; E2E migrated. Commit b07f029 |
| **U2** | `ino.cs` at root; start.cs per D1; flutter single-owner (deletions 1/2); beacon ownership move (6) | manual two-machine flow from README still works; run-ci green. Commit 902675c |
| **U3** | NeuronInstrumentation (§3.3) + `LlmNeuron`/`AgentNeuron` (§3.4) + persona merge (7) + emits-as-tools spike | traces visible per neuron in dashboard; LLM spans nested; high-sev green. Commit 0e3b710 |
| **U4** | Demo slices 1–3 (§3.5) incl. google-auth bundle + ported AuthGoogle.ino BDD + capability-grant surface (D3) | new BDD green; manual OAuth on real Google; no privileged emit without journaled grant. Commit 239c996 |
| **U5** | Bundle re-leveling (R4 microkernel pass, L1 capsules for kernel experiences) + first `.AsSilo()` per D4 + `UpgradeBundle` flow | install/upgrade of the L3 bundle with brain uptime preserved; N+1 invariants intact. Commit f348ace |

One commit per stage, deletions listed, DELETED/ROADMAP/USER-FLOWS/DISTRIBUTION updated, run-ci green at land — the standing ritual.

## 7. Landmines (do not relearn)

- Orleans serialization discipline everywhere new types appear (`[GenerateSerializer]` + `[Id(n)]`, concrete arrays, never collection expressions into `IReadOnlyList<T>`); every new union case through the collector/probe round-trip.
- L3 silos: the contract assembly must be on **every** silo + client that serializes those synapses — version the contract package independently of the implementation, and never let an implementation type leak into a synapse.
- Single-file AppHosts are CLI/VS Code only (no Visual Studio) — note it in README.
- `DistributedApplication` is only in DI for the AppHost process; kernel-side `IAspire`/`IFlutter` keep their "no DA in this activation context" branches (they already handle it).
- Don't trace-instrument the timeline `OnNextAsync` hot path with per-message spans *and* per-message synapse wrappers *and* journal appends without watching overhead — metrics are cheap, spans are not; sample if the dashboard chokes.
- Phase-1 durability honesty gap (in-memory `MinimalStateMachineManager`) is unchanged by this plan — U-stages must not write new assertions that assume journal durability that isn't there.
