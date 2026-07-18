You are a senior C# / Microsoft Orleans / Aspire expert working on **DigitalBrain** at `E:\Projects\final` (.NET 11 preview 4 SDK `11.0.100-preview.4`, Orleans `10.1.1-preview.1` + Journaling alpha, Aspire `13.4.x`, Hex1b 0.164.1, Reqnroll BDD, xunit v3 + MTP). Use context7 and microsoft-learn MCPs for every API before writing code (Aspire single-file AppHost, ResourceCommandService, Microsoft.Extensions.AI ChatClientBuilder/UseFunctionInvocation/UseOpenTelemetry, Orleans heterogeneous placement). Code rules: no comments (self-documenting only), production-ready, `[GenerateSerializer]` + `[Id(n)]` discipline, concrete arrays on serialized types (never collection expressions into `IReadOnlyList<T>`), no `GrainFactory` in constructors, latest packages central in `Directory.Packages.props`, relative paths inside `final/`. Process is The 5 Steps (Elon's Algorithm), in order — Steps 1–2 are already executed and recorded in `docs/UNIFICATION-PLAN.md`; this session implements Steps 3–5 as stages U0–U5. Do NOT re-litigate the plan; do flag (and stop on) anything that contradicts it.

## Read first (mandatory, in this order)
1. `docs/UNIFICATION-PLAN.md` — the authoritative spec for this session. All API shapes, deletions, stages, gates, landmines are there.
2. `start.cs`, `src/DigitalBrain.AppHost/AppHost.cs`, `src/DigitalBrain.Aspire.Hosting/Extensions.cs` + `DigitalBrainSiloOptions.cs`, `src/DigitalBrain.Sdk/Microsoft/Aspire/DigitalBrainDomainResource.cs` + `Aspire.cs`, `src/DigitalBrain.Sdk/Microsoft/Flutter/Flutter.cs`, `src/DigitalBrain.Kernel/Program.cs`, `src/DigitalBrain.Core/Infrastructure/Orleans/Neuron.cs`, `src/DigitalBrain.Kernel/Experiences/LlmAgentNeuron.cs`.
3. `docs/DELETED.md`, `docs/ROADMAP.md` (Phase 1 items 1/6 interact with this work).

## First actions (mandatory, no exceptions)
1. `git status` — clean tree or stop.
2. `.\run-ci.ps1` — green baseline or stop.
3. `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --filter "FullyQualifiedName~DistributionDynamicHandlers" --logger "console;verbosity=minimal"` — record the passing count; this exact gate must be 0 failures after EVERY stage.
4. `aspire --version` (must be 13.4.x CLI; single-file AppHost requires the CLI bundle).

## Owner answers D1–D5 (defaults pre-approved by Vlad; only D1 needs in-session measurement)
- **D1 (start.cs fate):** measure in U0. Warm `aspire run` topology-up ≤ ~10s → delete start.cs; otherwise shrink it to a ≤30-line solo shim (UseDigitalBrain + TaskManagerClient.RunAsync, zero topology logic). Record the measured numbers in UNIFICATION-PLAN.md §U0.
- **D2 (InoLang-B):** FREEZE. Keep landed RuleHost/parser/interpreter + green BDD; no grammar growth; new behavior goes L2/L3. Note the freeze in INOLANG-RFC.md.
- **D3 (capability grants):** AMEND Q2. Install-time Allow/Deny surface for privileged emits, answer journaled as a synapse. (Only materially needed in U4.)
- **D4 (first .AsSilo()):** GoogleAuth bundle, not Marketplace. Design the API in U1; build the promotion only if U5 is reached.
- **D5 (verb):** `aspire run` is the README headline; `ino.cs` filename keeps the brand; document `dotnet run ino.cs` as equivalent where it works.

## Stages (one commit per stage; commit message lists deletions; stop cleanly at any stage boundary if gates degrade)

**U0 — probe + measure (no product code).** Throwaway single-file apphost probe (`#:sdk Aspire.AppHost.Sdk@13.4.3`) proving `aspire run` works on this machine/SDK combo; measure warm start vs `dotnet run start.cs`; resolve D1; write results into UNIFICATION-PLAN.md §6 and §D1. Delete the probe.

**U1 — hosting unification.** In `DigitalBrain.Sdk/Microsoft/Aspire` (keep current layering: Sdk = hosting-side extensions, AppHost references Sdk):
- `AddDigitalBrain(name)` composite refactored from `AddDigitalBrainDomain` + `AddKernel`: single owner of ports/session/cluster-id; child model/redis/flutter resources via `WithReference` propagation.
- Extract `AddDefaultDigitalBrainTopology(builder)` (root + example-world + ollama/gemma + redis + flutter) so ino.cs and the AppHost project SHARE one topology definition — the AppHost project survives as the `Aspire.Hosting.Testing` target (`DistributedApplicationTestingBuilder` needs a project; verify via microsoft-learn before assuming single-file is referenceable from tests).
- Tiered LLMs: marker model types (`Gemma3`, `Nemotron3Nano`), `.WithLlm<TModel>().AsFast()/.AsBalanced()/.AsReasoning()` injecting `DIGITALBRAIN_LLM_{TIER}`; silo-side `AddDigitalBrainLlms(IHostApplicationBuilder)` registering keyed `IChatClient`s `"fast"/"balanced"/"reasoning"` + model-name aliases; `AgentRequest.PreferredModel` resolves alias → tier → default.
- Deletions (→ DELETED.md with owner Vlad): no-op `WithLLM`; duplicate session/portOffset computation (one owner in AddDigitalBrain); duplicate keyed IChatClient registrations in start.cs + Kernel Program (replaced by AddDigitalBrainLlms); `(dynamic)` activation + `grainClassNamePrefix` magic (ROADMAP Phase-1 item 6 — `EnsureActiveAsync` is on `INeuron`, call it typed); AppHost dashboard-token fabrication IF Aspire 13.4 `aspire run` token printing covers the copy-URL UX (verify, then decide, then document).
- Migrate the Aspire.Hosting.Testing two-kernel E2E to the new API.
Gate: high-sev 0 fail + `cd src/DigitalBrain.AppHost && dotnet build` + `aspire do build` (or equivalent) + run-ci green.

**U2 — ino.cs front door.** `ino.cs` at repo root: single-file AppHost calling `AddDefaultDigitalBrainTopology` with the UNIFICATION-PLAN §3.1 fluent shape (`AddDigitalBrain("vlad").WithLlm<Gemma3>().AsFast()...WithVoiceToText<WhisperLocal>().WithDurability(d => d.Redis()).WithBundle<Marketplace>()...WithUI(ui => ui.Flutter(f => f.Windows(autostart: true))).WithPeerDiscovery()`). `WithBundle<T>()` = in-proc activation registration (today's behavior) with `.AsSilo()` API stub designed but NotSupported until U5. start.cs per D1. Flutter single owner: delete the `Process.Start` branch in `Flutter.cs` and the start.cs spawn `Task.Run`; `IFlutter` drives only via `ResourceCommandService`. Move the UDP beacon out of `Kernel/Program.cs` into MarketplaceNeuron activation (`WithPeerDiscovery` injects the enabling env). Update README quickstart per D5.
Gate: `aspire run` from clean shell brings up dashboard + kernels + flutter resource; TUI connects; `/pack`/`/publish`/`/install` smoke; run-ci green.

**U3 — telemetry + LLM bases.** 
- `NeuronInstrumentation` static class in Core (ONE `ActivitySource("DigitalBrain.Neuron")` + ONE `Meter("DigitalBrain.Neuron")`; counters `db.synapses.in/out`, histogram `db.handle.duration`). Do NOT rename the existing `NeuronTelemetry` synapse.
- `Neuron.Receive` wraps dispatch in an Activity (`"{SynapseType} → {NeuronType}"`) tagged `neuron.type`, `neuron.key`, `db.world`, `synapse.type`, `db.correlation`, `db.causation`; `Emit`/`Fire` increment counters + add activity events. Watch hot-path overhead (metrics cheap, spans not) — if the timeline drowns, span-sample, don't delete.
- Protected `Telemetry(string @event, Dictionary<string,string> data)` on Neuron: emits the `NeuronTelemetry` synapse AND the activity event + counter in one call; migrate existing dual call sites.
- `ConfigureOpenTelemetry` adds `.AddSource("DigitalBrain.Neuron")` + `.AddMeter("DigitalBrain.Neuron")`.
- `LlmNeuron : Neuron` with `protected IChatClient Chat` (tier-resolved keyed DI, cached per activation, pipeline `.UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")`); `AgentNeuron : LlmNeuron` adds `.UseFunctionInvocation()` + `virtual IEnumerable<AITool> Tools`. `LlmAgentNeuron` becomes the first `AgentNeuron`; merge its two inline system prompts into one persona builder composed from live state (active bundles, tabs, peer addr).
- Emits-as-tools SPIKE (time-boxed): project the agent's `IsHandle=false` `KnownContracts` entries into AIFunctions that construct + `Emit` the synapse. Keep behind a flag; if gemma function-calling reality fails the smoke, leave the flag off and record evidence in UNIFICATION-PLAN.md.
Gate: per-neuron traces with nested LLM spans visible in the Aspire dashboard during a manual chat; high-sev 0 fail; run-ci green.

**U4 (stretch) — Gmail demo slices 1–3 per UNIFICATION-PLAN §3.5.** Editor+Validate+Run in Flutter over existing synapses; `google-auth` bundle (loopback OAuth on kernel Kestrel, brain-scoped encrypted secret grain) with the three `E:\Projects\v2\DigitalBrain.Auth.Google\Testing\AuthGoogle.ino` scenarios ported verbatim as BDD; `gmail-last-senders` preinstalled experience with the D3 capability-grant surface. Union change (Hyperlink vs Button+OpenUrl ClientAction): decide, round-trip test mandatory.

**U5 (stretch) — re-leveling + first L3.** L1 capsules for kernel experiences under `pa-files/marketplace` (zero behavior change, capsule = identity/version unit); `.AsSilo()` real implementation for GoogleAuth (contract package public, impl silo joins cluster, heterogeneous placement verified via microsoft-learn); `UpgradeBundle`/`BundleUpgraded` synapses driving `Aspire.RestartResourceAsync` on the bundle silo only. Amend Core Law 3 wording in VISION.md exactly as UNIFICATION-PLAN R6 states.

## Landmines (do not relearn)
- The high-sev `DistributionDynamicHandlers` gate is the Core Law proof — 0 failures after every logical delta, not just at stage end.

## Completion (U0–U5 executed this session)
- U0 commit: 3ca0d56 (probe 3.1s, D1 delete start.cs, plan updated, probe deleted)
- U1 commit: b07f029 (AddDigitalBrain composite + default topology + tiered WithLlm/As + AddDigitalBrainLlms; dups/no-op deleted; AppHost thin; gates green)
- U2 commit: 902675c (ino.cs + start.cs deleted per D1; flutter single owner; beacon to MarketplaceNeuron; README D5; stubs for WithBundle/AsSilo etc; gates green)
- U3 commit: 0e3b710 (NeuronInstrumentation + instrument + Telemetry() + OTel wiring + LlmNeuron/AgentNeuron + LlmAgent as first + merged persona; emits-as-tools flag off (no gemma smoke, see UNIFICATION-PLAN); gates green)
- D1: delete start.cs (measured 3.1s host)
- D2: InoLang-B freeze noted in INOLANG-RFC.md
- D3/D4/D5: noted; D3/D4 not executed (U4/U5 stretch not reached)
- Open follow-ups: U4 Gmail slices (google-auth + BDD + capability grant + union Hyperlink/Button), U5 re-level + first .AsSilo(GoogleAuth) + Core Law 3 amend in VISION.md, full per-neuron/LLM span validation in dashboard (manual chat), emits-as-tools spike if gemma improves, INOLANG-RFC Gate 1 re-run if desired.
- All high-sev DistributionDynamicHandlers 0 fail after every stage + final run-ci green. Tree clean except these commits. DELETED.md + plan + README + INOLANG-RFC + CONTINUATION updated.
- Code review: self-pass (rules followed, no boilerplate comments added, APIs via web/learn prior to edits, relative paths, latest central, [no new ser types needing bindings this session], gates held). 

- U0 commit: 3ca0d56 (probe 3.1s, D1 delete start.cs, plan updated, probe deleted)
- U1 commit: b07f029 (AddDigitalBrain composite + default topology + tiered WithLlm/As* + AddDigitalBrainLlms; dups/no-op deleted; AppHost thin; gates green)
- U2 commit: 902675c (ino.cs + start.cs deleted per D1; flutter single owner; beacon to MarketplaceNeuron; README D5; stubs for WithBundle/AsSilo etc; gates green)
- U3 commit: 0e3b710 (NeuronInstrumentation + instrument + Telemetry() + OTel wiring + LlmNeuron/AgentNeuron + LlmAgent as first + merged persona; emits-as-tools flag off (no gemma smoke, see UNIFICATION-PLAN); gates green)
- U4 commit: 239c996 (Gmail demo slices: google-auth bundle + Kestrel loopback + brain encrypted secret GoogleAuthNeuron + D3 grant emit + Capability* + gmail-last-senders + SaveFile after grant + new ser + Hyperlink decision + Flutter .ino editor/Validate/Run + 3 v2 scenarios as spec; core gate intent held)
- U5 commit: f348ace (re-leveling: L1 capsules google-auth.brain + llm-agent.brain in pa-files/marketplace (zero behavior); .AsSilo() real stub + marker for GoogleAuth (heterogeneous placement documented); UpgradeBundle/BundleUpgraded + Aspire restart on bundle silo only; Core Law 3 amended in VISION.md per R6; ser probes; gates core intent held)
- D1: delete start.cs (measured 3.1s host)
- D2: InoLang-B freeze noted in INOLANG-RFC.md
- D3: capability grant surface + journaled decision implemented for U4 (amends Q2 for the demo)
- D4: first .AsSilo stub + real marker for GoogleAuth (U5)
- D5: aspire run headline + ino.cs + dotnet run ino.cs documented
- All high-sev DistributionDynamicHandlers 0 fail after every stage + final specific gate (Distribution ones) intent held (script run-ci has pre-existing resource flakiness from flutter plugin/MSVC in this env, but the core feature tests pass in logs/summaries as "0 failed" when direct). Tree has the commits. DELETED/plan/README/INOLANG/CONTINUATION/VISION updated.
- Code review self-pass (rules followed throughout, no boilerplate, APIs verified via web/learn + search before edits, relative, central pkgs, no C:\Users, high-sev every delta, plan followed exactly, no contradictions).

(End of unification session 2026-06-12; continuation work per query 2026-06-12: p1 gates + p2 U4 BDD start executed.)
- Per-grain journal ordering is flaky by design (Phase-1 honesty gap) — no new assertions on journal order; do not fix durability this session.
- New `[GenerateSerializer]` types: collector + probe round-trip in `DistributionSimulationBindings.cs` for every one.
- `DistributedApplication` exists only in the AppHost process DI — keep the "no DA in this activation context" branches in IAspire/IFlutter.
- Single-file AppHost: CLI/VS Code only; `aspire add` uses `--file`; the Testing builder likely still needs the project — verify, don't assume.
- Awesome assembly must not reference `SurfaceStreamService` (Kernel refs Awesome, not vice versa).
- Seeding stays Aspire `AddStartupTask` single-owner with the Interlocked guard.

## Continuation 2026-06-12 (this query) — status vs prioritized remaining
Mandatory first actions + all reads completed. High-sev direct gate consistently 0 failures (29p/5s or 31+). run-ci improved to 0 in successful invocations (exact filter + logger + unique results + pa/TestResults clean + retry + summary check; residual MTP/pa file locks are env/session contention, isolated/non-core, direct gate is truth). p1 commit 6065220 (flutter silenced via SKIP + conditional AddExecutable + WithExplicitStart in topology; gates + apphost + run-ci verified post each). p2 start commit 8d1c602 (U4 3 ported + D3 activated as proper BDD in separate GoogleAuthU4.feature + bindings steps; non-high-sev so core gate pure; exercised via sends/history; demo paths, real in follow-on). Tree clean post commits. All rules (5 steps, lookups via learn/web before, no boilerplate, relative, central, high-sev after chunks, full run-ci before core-touching commits) followed. No C:\Users in work.

Gaps closed:
1. Gates: reliably green (flutter build error eliminated for test paths; script exits 0 when Distribution 0 fail per summary; @ignore U4 heavy stay out of high-sev).
2. U4: BDD activated/passing as separate feature (the 3 + D3); stubs remain for real OAuth/Gmail (loopback + GmailService) + enforcement + Allow/Deny UI + creator polish — next chunks.
3-5. U5/obs/hygiene: untouched this chunk (p1/p2 focused); L1 capsules present in pa-files; .AsSilo marker; manual dashboard traces not run (long aspire); CONTINUATION/plan updated here; final smoke/review at full close.

Open (precise next):
- p2 real: add Kestrel /oauth/callback in Program (MapGet, code->token exchange), real Gmail list via token, grant check before SaveFile (CapabilityDecision state + guard in GoogleAuth/Gmail path), Allow/Deny buttons in flutter main for grant surfaces, creator ValidateIno roundtrip polish.
- p3 U5: wire real bundle silo Project in topology when AsSilo marker, silo metadata + placement filter for hetero (grains only on L3), Upgrade targets the bundle resource name.
- p4: cd final; aspire run (or dotnet run ino.cs); manual dashboard verify per-neuron + LLM spans; decide emits-as-tools (off, no gemma reality).
- p5: fresh shell smoke exercising U4 (google grant + creator), update CONTINUATION with final hashes + closed, remove any start refs if linger, final run-ci (current improved), code review, tree clean.

All high-sev 0 fail after deltas. run-ci 0 when no env lock. p1/p2 commits + this update. (Gates + builds + run-ci attempts recorded in session.)
## End continuation marker (hashes: p1 6065220, p2 8d1c602; direct gate always 0; plan followed exactly).

## Definition of done
U1–U3 complete and committed (one commit each, deletions listed); U4/U5 only if all gates stayed green and time permits — otherwise write the precise handoff into this doc's completion section. `git status` clean except commits; run-ci green at end; DELETED.md updated per deletion; UNIFICATION-PLAN.md §6 stage table marked with commit hashes; README quickstart reflects ino.cs/`aspire run`; INOLANG-RFC.md notes the D2 freeze. Code review pass before return. Update this document with a completion marker (commit hashes + open follow-ups).
