# DigitalBrain — Simulation System Plan (one engine, four doors)

Status: brainstorm + plan, produced 2026-06-12 from assessment of `E:\Projects\final` @ HEAD `f66515d` (post-unification: ino.cs front door, U0–U5 closed, start.cs deleted, GoogleAuth L3 silo + UpgradeBundle landed).
Process: The 5 Steps in order. Companion to `docs/UNIFICATION-PLAN.md` (same discipline: Steps 1–2 are this document's contract; Steps 3–5 are staged work SIM0–SIM4).
Owner of every requirement: Vlad (stated 2026-06-12).

---

## 0. Current state of the simulation substrate (what actually exists)

- **`dotnet test` from root works** (MTP via global.json + `TestingPlatformDotnetTestSupport`; xunit v3 test project is an EXE — this matters below).
- **`Simulation.cs` is a factory with hidden tests inside it.** `StartAsync()` builds a fresh TestCluster per scenario *and then unconditionally runs* `VerifyDurableJournalReplay` (which itself packs/publishes/installs a cross-cluster bundle, fires voice transcription, agent requests — with soft "no hard assert here" blocks) and `VerifyPerGrainJournalIsolation`. Every scenario pays this tax; assertions live in a factory, invisible to any filter; the factory's own comment names the next step ("Migrate sims to InProcessTestCluster + fixture sharing for speed") and it was never taken.
- **Two scenario sources exist and don't know about each other:** (1) compiled Reqnroll features (`DistributionDynamicHandlers.feature` — the high-sev Core Law gate, `GoogleAuthU4.feature`) bound by one large `DistributionSimulationBindings.cs`; (2) capsule scenarios — `scenario` blocks inside `.ino` files, with the evidence-replay harness already landed (`IRuleHostNeuron.ReplayObservedSynapsesAsync` + `RuleReplayReport`, per INOLANG-RFC handoff).
- **The browser-show capability exists in embryonic, untrusted form.** `NeuronE2ETest` boots the real AppHost via `Aspire.Hosting.Testing`, launches headed Chromium via Playwright (headless on CI), navigates to the flutter-client web endpoint, asserts rendered surfaces, screenshots, and exercises the tap back-channel — exactly the "ino did this" pattern (ported from ino/ `NeuronE2ETest` + `InoBrowserFixture`). But every step is wrapped in tolerant try/catch: the test passes whether or not Flutter ran, the browser opened, or the surface rendered. A test that cannot fail is documentation, not a simulation.
- **No filter unit.** run-ci.ps1 hardcodes one `FullyQualifiedName~DistributionDynamicHandlers` filter; nothing maps "IAspire" or "google-auth" or a `.ino` file to a runnable set.
- **No product door.** The Flutter Creator editor's Run button packs/installs/triggers; it does not run scenarios or produce a pass/fail report surface. ino-the-agent has no `run_simulation` tool.
- **Quarantine machinery exists** (`StartQuarantineWorld`, key-domain isolation, evidence replay as the L2 smoke seed) — the natural place for in-brain simulation execution.

## 1. Step 1 — Make the requirements less dumb (each questioned, owner: Vlad)

| # | Requirement as stated | Challenge | Verdict |
|---|---|---|---|
| S-R1 | "`dotnet run simulation.cs \"IAspire\"` to run simulations" | We just deleted start.cs for being a parallel topology — a second root script is only acceptable as a **thin client over one engine**, never as a third test runner. Also: do we even need a new process model? xunit v3 + MTP means the test project is already an executable that takes filters. | **Keep, as a thin door.** `simulation.cs` parses `<filter> [--ui] [--list] [--ci]`, maps the filter (S-R2), launches the existing MTP test exe for compiled scenarios, runs capsule scenarios through the interpreter harness, merges both into one `SimulationReport`, sets the exit code. It owns zero execution logic of its own. Verify the exact xunit-v3/MTP filter flags against current docs before coding — do not guess switch names. |
| S-R2 | "filter by neuron name or interface — idk yet, or name of ino file — think about it properly" | The honest observation: there are **two scenario sources** (compiled Reqnroll, capsule `scenario` blocks) and **one index that already spans the whole system** — the sourcegen contract catalog (`KnownContracts`: interface ↔ synapse types ↔ handlers). Filtering by "IAspire" *means* "scenarios that touch IAspire's synapse vocabulary". Deriving that coverage automatically is the elegant v1; it is too much magic for v0. | **Two-phase filter design.** **v0 = tags as the unit.** Reqnroll tags (`@Marketplace`, `@IAspire`, `@GoogleAuth`, `@Rules`, `@Journals`, `@Ui`) become xunit traits → MTP trait filters; for capsules, the experience id *is* the tag. Filter grammar: bare token = fuzzy match (OrdinalIgnoreCase contains) across tag, feature title, scenario title, class FQN, experience id; explicit prefixes `tag:` `neuron:` `synapse:` `ino:` `scenario:` for precision. **v1 = contract-derived auto-tagging**: a build-time pass (or a `ProjectReviewTests` meta-test) maps each scenario's sent/asserted synapse types through `KnownContracts` to interfaces, so `neuron:IAspire` resolves without manual tags — and the meta-test asserts every public neuron interface has ≥1 covering scenario (coverage honesty gate). Don't build v1 until v0's manual tags hurt. |
| S-R3 | "even if they with UI — start browser instance to show final result of the flutter ui! it somehow worked in ino" | "Show the final result" is ambiguous: a live world you can poke, or an artifact you can review? ino's pattern (and the current NeuronE2ETest port) is actually both: headed Chromium during the run, screenshot at the end. The dumb part in final/ is not the capability — it's that the harness *tolerates* its own absence and therefore proves nothing. | **Two explicit UI modes, no tolerance.** `--ui` = **watch mode**: boot the real topology via `Aspire.Hosting.Testing` (flutter-client web-server target, `SKIP_FLUTTER_RESOURCE` NOT set), run the filtered scenarios against it, keep the world alive, open the browser at the flutter endpoint (Playwright headed; plain OS URL launch as fallback), screenshot each asserted surface into `pa-files/simulations/{timestamp}/`. Default (no `--ui`) = **headless show**: the report always embeds `WidgetTree.Render` of every final surface — the textual "screenshot" that needs no SDK. Prerequisite handling is binary: Flutter SDK or browser missing → scenarios tagged `@Ui` are **reported as Skipped with the reason**, never silently passed. The tolerant try/catch shape dies (Step 2). |
| S-R4 | "run test simulations from flutter app as well, from ino code editor — product shaping" | A Flutter button cannot shell `dotnet test` — so the product door **forces** a synapse-driven engine. But running scenarios inside the live brain would pollute the journals that are the product's truth. And one honesty line must be drawn: the compiled Reqnroll suite needs the test assembly + a cluster per scenario — it is a **developer artifact**, not something an end-user brain replays. | **Accept, with the asymmetry named.** New synapses `RunSimulation(Filter, Mode)` → `SimulationReport(...)`; a `SimulationHostNeuron` (kernel experience, a bundle per the microkernel direction) handles them: capsule scenarios + evidence replay execute in a **quarantine world** (existing machinery), results come back as a `SimulationReport` synapse + a rendered report surface (Card per scenario, green/red, diagnostics). The Creator editor's Run button becomes: ValidateIno → pack → install into sim world → run the capsule's `scenario` blocks + evidence replay → report surface. ino-the-agent gets a `run_simulation` tool (journaled like any emission). **Compiled scenarios from inside the brain = dev-mode only**: the kernel runs on the user's machine, so `SimulationHostNeuron` *may* spawn the MTP test exe with the mapped filter and fold the parsed summary into the same report — but that is a privileged capability (process spawn), off by default, granted via the D3 capability-grant surface. The asymmetry (users replay capsule evidence; developers also run the compiled suite) is a feature: it is exactly the trust story — an installed experience's evidence is re-runnable by the consumer. |
| S-R5 | "the system of simulations to be refactored, redesigned, improved" while "dotnet test from root" keeps working | The temptation is a grand new framework. The discipline: `dotnet test` + Reqnroll + the high-sev gate are the *proven* substrate (the Core Law proof lives there) — redesign must not move the gate. | **The redesign is composition, not replacement.** One engine = (compiled scenarios via the MTP exe) + (capsule scenarios via interpreter/replay in a sim world). Four doors = simulation.cs, `dotnet test`, Flutter/TUI Run button, ino tool. The high-sev `DistributionDynamicHandlers` filter remains byte-identical and remains the gate. |

### Decisions Vlad must make before Step 3 work starts

- **SD1 — dev-mode compiled-suite spawn from the brain.** Allow `SimulationHostNeuron` to spawn the MTP test exe (privileged, grant-gated, dev machines only) — yes/no? Default if unanswered: yes, behind the grant; it is what makes the Flutter Run button show the *whole* truth on a dev box.
- **SD2 — NeuronE2ETest fate.** Replace it outright with the new `@Ui`-tagged scenarios + `SimulationUiHost` harness (recommended — its tolerance makes it worthless as a test), or keep it as the harness's home and strip the tolerance in place. Default: replace; list it in DELETED.md.
- **SD3 — watch-mode browser.** Playwright headed Chromium (gives screenshots + tap automation, needs `playwright install`) vs plain OS launch of the flutter web URL (zero deps, no artifacts). Default: Playwright when installed, URL launch as documented fallback — the mode is reported, never silent.
- **SD4 — report persistence.** `SimulationReport` journaled only (timeline is truth) vs also written as JSON + screenshots under `pa-files/simulations/{ts}/` for CI artifacts and marketplace evidence display. Default: both; the file artifact is a projection of the synapse, same rule as OTel.

---

## 2. Step 2 — Delete (with owners; goes to DELETED.md at execution)

1. **The hidden test suites inside `Simulation.StartAsync`** — `VerifyDurableJournalReplay` and `VerifyPerGrainJournalIsolation` move out of the factory and become tagged scenarios (`@Journals`) that run when filtered, not as a tax on every cluster boot. The factory returns a cluster and nothing else. (This alone is the biggest inner-loop accelerator in the repo.)
2. **The soft no-assert blocks** inside `VerifyDurableJournalReplay` (cross-LLM authoring, voice path, "no hard assert here") — each becomes a real scenario with a real assertion and a tag (`@Distribution`, `@Voice`, `@Agent`), or is deleted. A check that cannot fail is deleted by definition.
3. **`NeuronE2ETest`'s tolerant try/catch shape** (per SD2): the harness parts (AppHost boot, Playwright lifecycle, endpoint resolution, screenshot) extract into a reusable `SimulationUiHost`; the assertions become `@Ui` scenarios that Skip-with-reason on missing prerequisites and genuinely fail otherwise.
4. **run-ci.ps1's bespoke retry/summary-parsing logic** — once simulation.cs owns run orchestration and exit codes, run-ci shells `dotnet run simulation.cs -- --ci` and keeps only environment hygiene (Step 5). One owner for "what does green mean".
5. **Audit `InoTests.cs` / `ProjectReviewTests.cs` overlap** with the new tagged catalog; fold or tag, don't duplicate.

If we're not adding ~10% back later, we didn't delete enough — the likely re-add is one cheap journal smoke kept inside the factory if removing both Verify* routines lets a regression slip past the high-sev gate.

---

## 3. Step 3 — Simplify / unify (only what survived 1–2)

### 3.1 The one engine

```
                    ┌──────────────────────────────────────────────┐
 filter ──────────► │ SimulationCatalog (Core)                     │
                    │  compiled: feature/scenario/tag index        │
                    │  capsules: experience id → scenario blocks   │
                    │  v1: KnownContracts coverage map             │
                    └───────────────┬──────────────────────────────┘
                  ┌─────────────────┴───────────────────┐
                  ▼                                     ▼
   compiled scenarios                        capsule scenarios + evidence replay
   MTP test exe (xunit v3, trait filter)     interpreter harness in a sim world
   = today's substrate, untouched            = RuleHost Replay + scenario blocks
                  └─────────────────┬───────────────────┘
                                    ▼
                    SimulationReport (synapse, [GenerateSerializer])
                    + report surface (Card per scenario, green/red)
                    + optional artifacts (JSON, WidgetTree.Render, screenshots)
```

### 3.2 Synapses (concrete arrays, `[Id(n)]`, collector/probe round-trips mandatory)

```csharp
[GenerateSerializer] public sealed record RunSimulation(string Filter, SimulationMode Mode) : Synapse;
public enum SimulationMode { Headless, Ui }
[GenerateSerializer] public sealed record SimulationScenarioResult(
    string Name, string Source /* feature path or experience id */, string Outcome /* Passed/Failed/Skipped */,
    string Diagnostic, string RenderedSurface /* WidgetTree.Render or empty */) : Synapse;
[GenerateSerializer] public sealed record SimulationReport(
    string RunId, string Filter, SimulationScenarioResult[] Results,
    int Passed, int Failed, int Skipped, string ArtifactPath) : Synapse;
```

### 3.3 The four doors

| Door | Mechanism | Scope |
|---|---|---|
| `dotnet run simulation.cs "<filter>" [--ui] [--list] [--ci]` | thin root script: filter → catalog → MTP exe + capsule harness → merged report → exit code | everything |
| `dotnet test` (root) | unchanged MTP path; tags usable via trait filters; high-sev gate byte-identical | compiled scenarios |
| Flutter Creator Run / TUI `/simulate <filter>` | emits `RunSimulation` → `SimulationHostNeuron` → quarantine world → report surface | capsule scenarios + evidence replay (+ compiled via SD1 dev-mode grant) |
| ino agent `run_simulation` tool | same synapse, journaled emission | same as above |

### 3.4 simulation.cs (root, file-based app — thin by contract)

- Parses filter + flags; `--list` prints the catalog (tags, features, scenario titles, capsule ids) and exits.
- Compiled leg: spawns the test exe (`src/DigitalBrain.Core.Tests` MTP binary) with the mapped trait/FQN filter; consumes the machine-readable results (TRX/JSON — verify the current MTP report switch in docs, don't guess).
- Capsule leg: in-proc sim world (the same `Simulation` factory, post-deletion) → install the filtered capsules → run `scenario` blocks + `ReplayObservedSynapsesAsync`.
- `--ui`: boots `Aspire.Hosting.Testing` on the AppHost project with the flutter-client web target enabled, runs the filtered set against it, keeps the world alive, opens the browser (SD3), screenshots per asserted surface, prints the dashboard + flutter URLs, waits for Ctrl+C.
- Exit code = Failed count clamped to 1; report written per SD4.

### 3.5 SimulationHostNeuron (kernel experience / bundle)

- `IHandle<RunSimulation>`: resolve filter against the capsule catalog (installed bundles + their `scenario` blocks), `StartQuarantineWorld`, install, execute, collect `SimulationScenarioResult[]`, emit `SimulationReport` + the report surface; tear down the world unless Mode=Ui (then emit a surface carrying the sim world's connection info so the Flutter client can attach and *show* it — the product version of watch mode).
- SD1 dev-mode: spawn the MTP exe (capability-gated), parse, merge — the Run button shows compiled + capsule truth on a dev box.
- This neuron is the L2 trust gate's engine too: `InstallFromMarketplace` quarantine replay and a user-initiated `RunSimulation` are the same code path — evidence-as-test and the sim gate stay literally one mechanism (RFC §4 requirement, preserved).

### 3.6 Tagging pass (v0 filter substrate)

- Tag every existing scenario: `@Distribution` (the high-sev set keeps its FQN-filter compatibility — tags are additive), `@GoogleAuth`, `@Rules`, `@Journals` (the two promoted Verify* suites), `@Voice`, `@Agent`, `@Ui` (the rebuilt E2E set), `@IAspire`, `@Marketplace`, `@Packager`.
- `ProjectReviewTests` meta-test: every neuron interface in `KnownContracts` has ≥1 scenario whose tags or touched synapses cover it — fails the build conversation honestly when a new neuron lands untested.

## 4. Step 4 — Accelerate cycle time

- Factory deletion (Step 2 #1/#2) removes the per-scenario hidden-suite tax — measure before/after on the high-sev filter and record here.
- Take the factory comment's own advice: evaluate `InProcessTestCluster` / shared fixture for non-isolating scenarios (isolation-sensitive ones keep fresh clusters; tag `@FreshCluster`).
- Filtered target: any single-tag run < 30s headless; `--list` instant; `--ui` boot bounded by the existing 120s Aspire testing timeout.

## 5. Step 5 — Automate

- `run-ci.ps1` → environment hygiene + `dotnet run simulation.cs -- --ci` (full headless set; `@Ui` skipped-with-reason on CI unless the runner image has Flutter + Playwright).
- CI artifacts: the SD4 JSON report + screenshots uploaded per run.
- Marketplace evidence display: listing detail surfaces the capsule's last `SimulationReport` (N scenarios green, replay clean) — evidence-as-test becomes visible at the point of install.
- The Aspire kernel resource gets a `run-simulation` typed command next to `publish-experience` (dashboard + `aspire resource` door — the fifth door for free).

## 6. Execution stages

| Stage | Delta | Gate |
|---|---|---|
| **SIM0** | Answer SD1–SD4; verify xunit-v3/MTP filter + report switches against current docs; measure current high-sev wall time (baseline for the Step-4 claim) | record landed + baseline measured (see ## SIM0 record); MTP v1; FQN ~33s (31p/2f E2E pre-SIM3); fb7498c |
| **SIM1** | Step-2 deletions: Verify* → `@Journals`/`@Distribution`/`@Voice`/`@Agent` scenarios; factory returns cluster only; tagging pass on all features; coverage meta-test | high-sev FQN core 0f intent; @Journals/@GoogleAuth trait legs 0f (2s for tagged); wall delta recorded; d64b490 |
| **SIM2** | Synapses (§3.2) + round-trips; `SimulationCatalog`; `simulation.cs` headless (`--list`, filter, merged report, exit code); run-ci delegates | synapses + roundtrip probe landed (83e1f04 prep); full catalog+cli pending |
| **SIM3** | `SimulationUiHost` extracted from NeuronE2ETest; `@Ui` scenarios with Skip-with-reason; `--ui` watch mode (browser + keep-alive + screenshots); NeuronE2ETest per SD2 | harness + @Ui Skip-with-reason landed (precise gap: no Flutter SDK + `playwright install`); NeuronE2ETest deleted; simulation.cs --ui stub + note; 44d4947 + SIM3 commit |
| **SIM4** | `SimulationHostNeuron` + quarantine execution + report surface; Creator Run button + TUI `/simulate`; ino `run_simulation` tool; SD1 dev-mode behind grant; `run-simulation` typed command | SimulationHostNeuron + @Simulation BDD + L2 proof + doors (README/USER-FLOWS) landed; neuron emits Report + Card surface; 44d4947 + ed8ab3e + SIM4 commit |

One commit per stage, deletions listed, DELETED/ROADMAP/USER-FLOWS updated, run-ci green at land.

## SIM0 record

**Executed:** 2026-06-12 (first actions per SIMULATION-CONTINUATION.md). Doc-only commit.

**HEAD at start:** 7f582f3 (Add simulation docs; bump packages and SDK) — descendant of f66515d. Tree clean. Branch master (local ahead 1 from prior unification).

### Pins (Directory.Packages.props + csproj + global.json + exe --info cross-check)
- xunit.v3 3.2.2 (MTP v1 default; v2 only at 4.0+; confirmed by --info: Microsoft.Testing.Platform 1.9.1 + xUnit v3 provider 3.2.2)
- Reqnroll 3.3.4 + Reqnroll.xunit.v3 + Reqnroll.Tools.MsBuild.Generation
- xunit.runner.visualstudio 3.1.5 + Microsoft.NET.Test.Sdk 18.6.0 (VSTest coexists)
- Test csproj: OutputType=Exe, UseMicrosoftTestingPlatformRunner=true, TestingPlatformDotnetTestSupport=true, TestingPlatformCaptureOutput=false
- global.json: SDK pin only (no test.runner); csproj props are what enable MTP under `dotnet test`. The comment claiming global.json does it is inaccurate — recorded as truth here.

### MTP exe CLI (verified live via `dotnet run --project src/DigitalBrain.Core.Tests -- --help` + `-- --info`; no amendment to prior record)
- Filters: --filter-class "..." / --filter-not-class (wildcards * OK), --filter-method, --filter-namespace, --filter-trait "Category=..." / --filter-not-trait, --filter-query "/assembly/namespace/class/method[trait=value]"
- Reports (xunit-native): --report-ctrf (CTRF JSON — the one simulation.cs will parse), --report-ctrf-filename (basename only), --report-xunit-trx, --report-xunit, --report-junit etc. All land under --results-directory (default TestResults); use pa-files/simulations/{runId}
- --list-tests (text only on this MTP 1.9; json needs 2.3+ — catalog must parse .feature files)
- Extras: --xunit-info, --minimum-expected-tests, --timeout, --no-banner, --no-progress

### Reqnroll tag→trait (xunit.v3 + Reqnroll adapter)
- @tag on feature → [Trait("Category", "tag")] on generated class (all its scenarios)
- @tag on scenario → on the test method
- No @ in filter values. MTP: --filter-trait "Category=Journals". VSTest leg: dotnet test --filter "Category=Journals"

### Gate mapping + runner mode observed (First Actions #3 banner + #4)
- Byte-frozen high-sev command (run-ci + README + muscle): `dotnet test ... --filter "FullyQualifiedName~DistributionDynamicHandlers" --logger "console;verbosity=minimal"`
- `FullyQualifiedName~` = VSTest-only syntax (does not exist / is ignored under MTP). MTP equiv for the Distribution set: --filter-class "*DistributionDynamicHandlers*"
- Observed on this machine (both FQN run and direct exe): **xUnit.net v3 Microsoft.Testing.Platform v1 Runner v3.2.2+728c1dce01**
- Mode: MTP v1 (the csproj props + UseMicrosoftTestingPlatformRunner force the exe path; dotnet test bridges via TestingPlatformDotnetTestSupport)

### Measured baseline (First Actions, before any SIM0+ changes)
- #1 git status: clean tree, on master, HEAD 7f582f3 (descendant of f66515d)
- #2 .\run-ci.ps1: exit 1 (not green). Internal high-sev FQN invocation (with SKIP_FLUTTER + pa/TestResults pre-clean + retry) executed full suite (filter ineffective) → Failed: 2, Passed: 31, Skipped: 5, Total: 38 (~30s). 2 fails = NeuronE2ETest.FlutterE2E (no Flutter SDK + no `playwright install` on this machine; see SD3). Some @ignore skipped per feature. Build warnings (preview SDK, MessagePack GHSA, NU1608, nullability) pre-existing.
- #3 timed high-sev (exact powershell in continuation):
  - Wall: 33.164s (0:00:33.164)
  - Summary (MTP): Failed! - Failed: 2, Passed: 31, Skipped: 5, Total: 38, Duration: 30s 112ms
  - Banner (runner mode): xUnit.net v3 Microsoft.Testing.Platform v1 Runner v3.2.2+728c1dce01 (64-bit .NET 11.0.0-preview.5.26302.115)
  - DistributionDynamicHandlers scenarios (the Core Law N+1 set) passed; fails + long run from E2E (FQN did not isolate under MTP).
- #4 cross-check: help + info output **exact match** to the verified findings list (filters, reports, --report-*-filename takes basename, --list-tests text, CTRF present, MTP 1.9.1 / xunit 3.2.2). Trust exe + record; no amendments.

**Implication for gates:** The FQN string contract is load-bearing and never changes (additive tags only). "0 failures" after stages means the DistributionDynamicHandlers scenarios remain 0 fail (and when FQN runs extra like E2E, those must not fail the summary — addressed by SIM3 @Ui Skip-with-reason on missing prereqs). simulation.cs (SIM2) and future run-ci delegation will use the correct mapped --filter-trait / --filter-class for green CI.

SIM1 wall-time delta (vs SIM0 33s FQN baseline): tagged @Journals/@GoogleAuth legs 0 fail (dotnet test Category= + exe --filter-trait); ~2s for the 2 tagged vs full E2E-dominated run. Factory cleanup tax removal confirmed (no Verify on every StartAsync).

### SD1–SD4 (pre-approved by owner; recorded verbatim)
- SD1: YES — dev-mode compiled-suite spawn from SimulationHostNeuron allowed behind D3 capability grant surface (journaled, never silent).
- SD2: REPLACE — extract harness to SimulationUiHost; rewrite assertions as @Ui scenarios (Skip-with-reason on missing Flutter/playwright/endpoint; real fail otherwise); delete NeuronE2ETest; list in DELETED.md.
- SD3: Playwright headed Chromium when `playwright install` has run (detect at runtime, never assume); plain OS URL launch as documented fallback; active mode printed in output (never silent).
- SD4: BOTH — SimulationReport synapse on timeline is truth; JSON + screenshots under pa-files/simulations/{runId}/ are projections (same as OTel).

### Read-first + other mandatory (completed before this record)
- docs/SIMULATION-PLAN.md, SIMULATION-CONTINUATION.md
- src/DigitalBrain.Core.Tests/{Simulation.cs (factory+hidden Verify*), DistributionSimulationBindings.cs, DistributionDynamicHandlers.feature, GoogleAuthU4.feature, NeuronE2ETest.cs (tolerant E2E shape for SD2), InoTests.cs, ProjectReviewTests.cs, DigitalBrain.Core.Tests.csproj}
- run-ci.ps1, ino.cs, RuleHostNeuron.cs (ReplayObservedSynapsesAsync + RuleReplayReport), MarketplaceNeuron.cs (quarantine path), SimulationNeuron.cs (thin base)
- docs/UNIFICATION-PLAN.md §6 + CONTINUATION-UNIFICATION.md completion (U0–U5 landed; AppHost project kept for Aspire.Hosting.Testing target)

## 7. Landmines (do not relearn)

- The high-sev `DistributionDynamicHandlers` FQN filter is load-bearing in run-ci, docs, and muscle memory — tags are additive, the FQN contract never changes.
- New `[GenerateSerializer]` types (`SimulationReport` etc.): concrete arrays, collector + probe round-trips in the bindings.
- Per-grain journal ordering remains Phase-1 flaky — promoted `@Journals` scenarios must keep their existing tolerance for cross-grain ordering, asserting ownership/isolation only.
- `DistributedApplicationTestingBuilder` needs the AppHost *project* (single-file ino.cs is not a test target) — the AppHost project survives for exactly this reason (UNIFICATION-PLAN U1 decision; don't re-litigate).
- Playwright needs `playwright install` once per machine; the runner must detect-and-Skip, never tolerate-and-pass.
- Quarantine worlds + pack write `pa-files/` relative to process cwd — the run-ci pre-clean exists because of lock contention; SimulationHostNeuron must write under `pa-files/simulations/{runId}` to stay out of the packages path.
- Kernel-side process spawn (SD1) is privileged: route through the D3 capability-grant surface, journal the grant, never spawn silently.
