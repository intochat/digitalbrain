# CONTINUATION — Whole-Repo Cleanup & Simplification (The 5 Steps)

Status: PROPOSED — owner decisions D-CL1…D-CL8 below. No deletions performed yet.
Repo: `E:\brain`. Date: 2026-07-04.
Anchors: `docs/PRODUCT_VISION.md` (2026-07-01), `docs/CONTINUATION-MULTIUSER-IDENTITY.md` (2026-07-04),
`docs/SYSTEM_DESIGN.md`, `CONTINUITY.md`.

Every claim below was verified against the current tree (directory listings + file reads on
2026-07-04), not assumed. Method: Musk's 5 steps, **in order** — requirements → delete → simplify →
accelerate → automate. Steps 4–5 are deliberately thin; doing them before 1–3 would lock in waste.

---

## 0. Architecture assessment (what the tree actually is today)

The core is sound and matches both vision docs:

- **Protocol** (`DigitalBrain.Core`): `INeuron`/`Synapse`/`IHandle<T>`, causal lineage, checkpoints. Small, guarded by architecture tests.
- **Pack rail** (Foundry `FoundryCompilation`/`PackAlcEmbodier`/`CapabilityGate` + `GeneratedNeuron` + `MarketplaceNeuron` + signing/trust): the N+1 embodiment mechanism — the product's spine.
- **Delivery** (Gateway gRPC + `HomeFeedBus`/`SignalEgressBus` + UiKit/RFW + Telegram transport): matches PRODUCT_VISION §4.3.
- **Identity seed** (`UserSessionNeuron`) and integrations (Salesforce/Google/Ino): the subject of CONTINUATION-MULTIUSER-IDENTITY, problems P1–P6 confirmed in code (callback in `Program.cs` doing store IO + token exchange; the startup warmup block is literally an inventory of the `"*-main"` singletons P2 describes).

The problem is everything welded around that core. `DigitalBrain.Kernel` carries **at least four
strata of demo/prototype code as first-class kernel neurons**, in direct violation of the
platform's own law (PRODUCT_VISION §4.4: content ships as bundles, not kernel internals):

| Stratum | Evidence in tree |
|---|---|
| "Software 2.0 / closed loop" era | `Software20TeamNeuron`, `SoftwareEngineeringClosedLoopNeuron`, `CompilerNeuron` (builds a `NeuroPack` and **discards it** — dead code path), `InoCodeEditorNeuron`, `Awesome/` (+ `Core/Awesome/ReviewSynapses.cs`), Foundry closed-loop neurons (`CodeGen`/`CodeRun`/`CodeDeploy`/`CodeFoundryClosedLoop`, `AzureResourceController`) |
| "CompanyBrain" era | `Company/` (`CompanyKnowledgeNeuron`, `CompanySkillOrchestratorNeuron` — reads `samples/CompanyBrain/refund-policy.md` off disk, `ProcessCrystallizer`, `SkillPackSynthesizer`), `Core/CompanySkillSynapses.cs` |
| "Chat-with-your-data" demos | `Db/SqliteSchemaInspector` + `DbSupportNeuron`, `TabularData/TabularDataParser`, `Uploads/ChatUploadClassifier` + the `/upload` endpoint (hardwired to `"ino-main"`/`"db-main"` singletons, temp files on kernel disk — a P6-class multi-user leak), `DataVisualizationNeuron` (13 KB `ChartNeuron`), `Ui/ChatNeuron` (a **second** handler of `VisualizeDataRequest`), regex intents inside `InoNeuron` (`IsBitcoinPriceIntent`, `IsTwoObjectRelationIntent`, `IsSchemaVisualizationIntent`) |
| One-day team demo (2026-07-03) | `Market/` (CoinGecko client + `MarketDataNeuron`), `simulate_x_post` MCP tool, `XBitcoinTelegramDemoNeuron`/`KeywordWatcher` seeds, warmup line for `"market-data-main"` |

Plus structural debris: `LlmNeuron` duplicates `LlmResponderNeuron`; `DemoNeuron` +
**two whole projects** (`DigitalBrain.Demo.Contracts`, `DigitalBrain.Demo.Runtime`) exist to carry
a hello-world, and `FoundryCompilation` compiles *every* pack against `Demo.Contracts`;
`RemoteMarketplaceClientStub` + `UseRemote` flag stub a marketplace service repo that doesn't
exist; `Sandbox/` implements the isolation PRODUCT_VISION §1.3 explicitly defers to Phase 2;
`Core/Synapse.cs` is an 18 KB god-file; the `Sdk/` folder puts `ShellNeuron`/`WingetNeuron`/
`FileSystemNeuron` (arbitrary process execution + file IO) on the same broadcast timeline the
multi-user plan wants to open to multiple users.

Scale: 36 C# projects + Flutter app; 25 Kernel subdirectories; ~20 top-level Kernel neuron files.
Roughly **half the Kernel's neuron surface serves no requirement in either anchor doc.**

The two anchor docs describe two products — creator platform (PRODUCT_VISION) and personal
assistant with CRM/Google (MULTIUSER). These are reconcilable — the assistant is first-party
*content on* the platform — but today the assistant's features bypass the platform's own
distribution rail and live as kernel welds. That contradiction is the root of most of the trash.

---

## Step 1 — Make the requirements less dumb

Every questioned requirement, its origin (a person/session, not a vague department), and verdict:

- **R1 — "Assistant features live as hardcoded kernel neurons + regex intents in InoNeuron."**
  Origin: successive demo sessions, most recently Vlad's own "show MVP to my team asap"
  (CONTINUITY 2026-07-03). Verdict: **dumb as a permanent state.** Contradicts PRODUCT_VISION §4.4
  and blocks MULTIUSER S5 (every intent is another thing `{userId}/{threadId}` migration must carry).
  New requirement: *no feature enters the Kernel as a neuron if it can be a pack/bundle* (D-CL1).
- **R2 — "The shared kernel hosts local-OS capabilities (Shell/Winget/FileSystem/Git/DotNet/NuGet/Roslyn)."**
  Origin: the personal-AI-OS era (pre-vision-doc). Verdict: **dumb and dangerous for the multi-user
  direction.** A shared cloud kernel where any broadcast can reach `ShellNeuron` is remote code
  execution by design; MULTIUSER I1/I2 isolate *data*, not *capabilities*. Must be resolved before
  S2 opens the gateway to more users (D-CL2).
- **R3 — "Keep a sandbox implementation now."** Origin: Foundry README hardening ambitions.
  Verdict: dumb per the vision's own non-goal list (§1.3: sandbox is "explicitly Phase 2… deliberately
  not solving yet"). A just-in-case hedge; git preserves it.
- **R4 — "Keep a remote-marketplace client stub behind a `UseRemote` flag."** Origin: an anticipated
  separate private-marketplace repo. Verdict: hedge for a repo that doesn't exist. Delete.
- **R5 — "Every pack compiles against `Demo.Contracts`."** Origin: convenience when the demo
  protocol was extracted from Core. Verdict: dumb coupling — the demo assembly is in the trusted
  compile-reference set of every third-party pack forever. Fold demo content into `SeedPacks` (D-CL4).
- **R6 — "Two LLM entry points (`LlmNeuron` + `LlmResponderNeuron`)."** Origin: pre-`AskLlm`
  prototype left behind. Verdict: one must die; `LlmResponderNeuron` is the one production and the
  Telegram experience use.
- **R7 — "Journals are never deleted (Core Law 2)" as applied to *dev/demo* journal storage.**
  Origin: owner (Core Law 2). Verdict: the law is right for user truth; it is dumb when it forces
  keeping *synapse type definitions* for deleted demo features so orleans-binary journal replay
  doesn't break. Resolution needed as D-CL7 (one-time storage reset vs. legacy-type quarantine).
- **Kept requirements (questioned, survived):** pack signing/trust gates; kernel self-update via
  rolling HA; `[GenerateSerializer]` + sequential `[Id(n)]` discipline; the authoring loop as the
  moat (Phase 0); Economics/Stripe (vision §7.7 — built, small, load-bearing for the marketplace
  story); `KernelTaskNeuron` (real Ino UX for spawned tasks — revisit shape in S5, don't delete).

---

## Step 2 — Delete (the bulk of this plan)

Rule: deletion, not commenting-out, not `#if`. Git history is the archive. Target: if we aren't
re-adding ~10% of this later, we didn't cut hard enough — a re-add ledger is at the end of this doc.

### Wave D1 — dead demo strata (zero product risk)

| Delete | Also remove (blast radius) |
|---|---|
| `Kernel/Market/` (CoinGecko, `MarketDataNeuron`) | `AddHttpClient<IMarketDataApiClient…>` + `market-data-main` warmup in `Program.cs`; `IsBitcoinPriceIntent` block + `DigitalBrain.Kernel.Market` using in `InoNeuron`; `simulate_x_post` MCP tool; `XBitcoinTelegramDemoNeuron` + `KeywordWatcher` seeds in `MarketplaceSeeds.cs`; `Tests/Market/`, `XBitcoinTelegramDemo.feature` |
| `Kernel/Company/` + `samples/CompanyBrain/` | `ProcessCrystallizer`/`SkillPackSynthesizer` DI registrations in `Program.cs`; `Core/CompanySkillSynapses.cs`; `Tests/Company/` |
| `Kernel/Awesome/` + `Core/Awesome/` | `Tests/Awesome/`, `AwesomeSoftware20.feature`, related steps |
| `Software20TeamNeuron`, `SoftwareEngineeringClosedLoopNeuron`, `CompilerNeuron`, `InoCodeEditorNeuron` | Their interfaces/synapses in `Core/Synapse.cs`; `ino-editor-main` warmup line |
| Foundry closed loop: `CodeFoundryClosedLoopNeuron`, `CodeGenNeuron`, `CodeRunNeuron`, `CodeDeployNeuron`, `AzureResourceController` (+ `ICodeExecutor`/`IBuildRunner`/`IResourceController`/`InProcessAlcExecutor` if their only consumers are these) | `run_closed_loop` MCP tool; `Core/CodeFoundrySynapses.cs`; `CodeFoundry.feature`; `Tests/Foundry/` closed-loop cases. **Keep** `FoundryCompilation`, `PackAlcEmbodier`, `CapabilityGate`, `AddFoundry()` — the production pack rail (D-CL3) |
| `Kernel/Sandbox/` | `Tests/Sandbox/` (R3) |
| `Kernel/Marketplace/RemoteMarketplaceClientStub` + `IRemoteMarketplaceClient` + `UseRemote` config branch | (R4) |
| `LlmNeuron` (`LlmPrompt`/`LlmResponse` path) | Its synapses/interface in Core if unreferenced after the demo strata go (R6) |
| `DemoNeuron` | `IDemoNeuron` warmups/tests referencing it |

### Wave D2 — feature strata → future bundles (delete now, re-add as packs on demand)

| Delete | Also remove |
|---|---|
| `Db/SqliteSchemaInspector`, `DbSupportNeuron` | `SqliteSchemaInspector` DI registration; `db-main` warmup; `Microsoft.Data.Sqlite` + `SQLitePCLRaw` package refs; `Tests/Db/`; the SQLite branch of `/upload` |
| `TabularData/`, `Uploads/ChatUploadClassifier`, the whole `/upload` endpoint | `ClosedXML` package ref; `TabularDataSynapses.cs` in Core; `Tests/TabularData/`, `Tests/Uploads/` |
| `DataVisualizationNeuron` (`ChartNeuron`) **and** `Ui/ChatNeuron` (duplicate `VisualizeDataRequest` handlers) | `chart-main` warmup; chart/graph synapses; Dart-side cards in `app/` that only these emit (`DataChartCard`, DB-schema graph surfaces) — audit `ui:*` registry usage after deletion |
| `InoNeuron` demo intents: Bitcoin, two-object relation graph, schema visualization, tabular ingestion handling | Shrinks `HandleAsync` to LLM + Gmail/Salesforce shortcuts; makes MULTIUSER S5's `TurnPipeline` replacement land on a far smaller surface |

Rationale: these are real features, but per D-CL1 they must be content bundles riding the
marketplace rail (which is literally the product being sold). Deleting them from the kernel is not
losing them — it is forcing the platform to dogfood itself when they return.

### Wave D3 — mode split for local-OS capabilities (needs D-CL2 first)

`Kernel/Sdk/` (7 neurons) + their ino projects `DigitalBrain.Windows`, `DigitalBrain.Developer`
(+ `.Tests`, + the `FileSystemOperations`/`RoslynAnalysisService` DI registrations, +
`Microsoft.CodeAnalysis.Workspaces.MSBuild` package). Proposed: **delete from the shared kernel
build entirely.** The authoring loop (the moat) runs on the dev machine via `dotnet test`, not via
kernel SDK neurons; nothing in PRODUCT_VISION or MULTIUSER consumes them. If a local
single-user "personal OS" mode becomes a real requirement again, they return from git as a
local-mode-only registration — that re-add is pre-budgeted in the 10% ledger.

### Wave D4 — project consolidation

- Fold `DigitalBrain.Demo.Contracts` + `DigitalBrain.Demo.Runtime` into `DigitalBrain.SeedPacks`
  (hello-world source, signing helper, demo graph surface) per D-CL4; remove `Demo.Contracts` from
  `FoundryCompilation`'s compile-reference set (R5).
- Expected project count: 36 → **30–31**. Expected Kernel subfolders: 25 → **~14**.
  Kernel top-level neuron files: ~20 → **~8**.

### Deletion mechanics (every wave)

1. One `spec/cleanup-wave-N` branch per wave; delete + fix references in the same commit series.
2. `dotnet build` → targeted `dotnet test --filter` for touched areas → one full suite run per wave.
3. Delete the wave's seeds/MCP tools/`.feature` files/test folders *in the same wave* — a seed
   referencing a deleted grain key is a runtime break the compiler won't catch.
4. `CONTINUITY.md` entry per wave; this doc's checklist updated.

---

## Step 3 — Simplify or optimize (what remains — only after D1–D4 land)

- **`Program.cs` (24 KB) shrinks structurally, not cosmetically:** `/upload` gone (D2); the
  Salesforce callback moves into the grain per MULTIUSER **S1** (parse-and-route endpoint);
  the warmup block drops `ino-editor-main`/`db-main`/`chart-main`/`market-data-main` and becomes a
  short, honest list of real timeline subscribers.
- **Split `Core/Synapse.cs` (18 KB god-file)** into concern files (protocol core / pack+marketplace /
  task protocol). Same assembly, no behavior change. Deleting D1/D2 synapse types shrinks it first.
- **One grain-key registry.** Adopt MULTIUSER's `NeuronKeys` for *all* well-known keys; today
  literals like `"market-main"`, `"ino-main"`, `"session-main"` are scattered call-site strings.
- **UiKit audit:** after D2, count `ui:*` covers actually emitted; delete unused Dart covers in
  `app/` (the 39-cover vocabulary predates the deletions).
- **Ordering with MULTIUSER:** run S1 first or in parallel (it is a permanent race fix, independent
  of cleanup), then D1–D4, then S2–S5. Rationale: don't apply per-user keying and gateway identity
  to neurons that are about to die (D-CL6).

## Step 4 — Accelerate cycle time

The deletions *are* the acceleration: fewer projects to build, fewer TestCluster boots, fewer
`.feature` scenarios. Measure before/after per wave: `dotnet build` wall time, full-suite test
count + wall time, Kernel image size. Keep the existing warm-cluster attach and
`e2e.runsettings` slices unchanged. No new speed infrastructure until deletes land.

## Step 5 — Automate (last)

Extend the existing Architecture guard tests to lock the cleaned state in:
- Core contains no feature synapses (assert file/type inventory).
- Kernel's `IHandle<T>` registrations match an approved neuron list (fails when someone welds a
  new demo neuron in instead of shipping a pack — the executable form of D-CL1).
- No well-known grain-key string literals outside `NeuronKeys`.
Publish-on-green stays as PRODUCT_VISION already defines it. Nothing else automated.

---

## Owner decisions

- **D-CL1** — Law: the assistant (Ino + integrations) is first-party content *on* the platform.
  Existing welds get deleted or bundled per waves above; **no new feature enters the Kernel as a
  neuron if it can be a pack.** Guard test in Step 5 enforces it. [PROPOSED]
- **D-CL2** — Local-OS SDK neurons (`Shell`/`Winget`/`FileSystem`/`Git`/`DotNet`/`NuGet`/`Roslyn` +
  `Windows`/`Developer` inos): delete from the shared kernel (Wave D3). Alternative (rejected as a
  hedge): keep behind a local-mode flag nobody currently uses. [PROPOSED]
- **D-CL3** — Foundry: keep only the pack rail (`FoundryCompilation`/`PackAlcEmbodier`/
  `CapabilityGate`); delete the closed-loop generation neurons + `AzureResourceController` +
  `run_closed_loop`. PRODUCT_VISION calls in-app authoring "Phase 2… the rails exist" — the rails
  that matter (compile/embody/publish) survive; the LLM-generation wrappers return from git if
  Phase 2 arrives. [PROPOSED]
- **D-CL4** — `Demo.Contracts`/`Demo.Runtime` fold into `SeedPacks`; `Demo.Contracts` leaves the
  pack compile-reference set. [PROPOSED]
- **D-CL5** — Charts/DB/tabular/upload: delete now (Wave D2); re-add as content bundles on real
  demand, charged to the 10% ledger. [PROPOSED]
- **D-CL6** — Sequencing: MULTIUSER **S1** → cleanup **D1–D4** → MULTIUSER **S2–S5** → Step 3
  simplifications interleaved where they touch the same files. [PROPOSED]
- **D-CL7** — Journal compatibility for deleted synapse types (orleans-binary replay): accept a
  **one-time dev/prod storage reset** before MULTIUSER S2 (identity spine restarts the world
  anyway; there are no external users yet), rather than a legacy-type quarantine assembly. If this
  is wrong about prod journal value, choose quarantine instead — decide explicitly. [PROPOSED]
- **D-CL8** — The parked `spec/self-improvement-loop` branch: delete the branch (git reflog keeps
  it recoverable for 90 days; the idea is recorded in CONTINUITY 2026-07-03). [PROPOSED]

## Risks / notes for the implementing session

- **Journal replay is the sharp edge** (D-CL7): durable Azure-blob journals contain orleans-binary
  serialized instances of types being deleted. Do not merge D1/D2 against an environment whose
  journals must survive without resolving D-CL7 first.
- **Seeds and MCP tools are runtime references the compiler won't catch** — `MarketplaceSeeds.cs`
  pack source strings and MCP tool wrappers name grain keys and synapse types as strings. Grep for
  every deleted grain key and synapse type name across `SeedPacks`, `Mcp`, `app/` (Dart), and
  `.feature` files before declaring a wave done.
- **Warmup block is load-bearing** — broadcasts only reach activated grains; when deleting a warmup
  line, delete the grain; when keeping a grain that must hear broadcasts, keep its line (the
  MarketDataNeuron incident in CONTINUITY 2026-07-03 is the cautionary tale).
- **Two `VisualizeDataRequest` handlers** (`ChartNeuron`, `ChatNeuron`) means deleting only one
  still leaves the synapse live — D2 deletes both plus the synapse.
- Filesystem MCP quirk: never `list_directory` the repo root; targeted subdirectory listings +
  `read_multiple_files` batches.
- `[GenerateSerializer]` + sequential `[Id(n)]` discipline applies to any synapse *modified* (not
  just added) while splitting `Core/Synapse.cs`.

## 10% re-add ledger (append-only)

Deliberately empty at creation. Every time something deleted by this plan is re-added, record it
here with date + reason. If this ledger stays empty for months, the next cleanup should cut deeper.

| Date | Re-added | From wave | Why |
|---|---|---|---|

## Acceptance metrics (capture before Wave D1, re-capture after D4)

| Metric | Before (2026-07-04) | Target |
|---|---|---|
| C# projects | 36 | ≤ 31 |
| Kernel subdirectories | 25 | ~14 |
| Kernel top-level neuron files | ~20 | ~8 |
| `Program.cs` size | 24.1 KB | < 12 KB |
| Startup warmup grain list | 8 entries | ≤ 4 |
| `.feature` files | 6 | 3 (`NeuronCore`, `MarketplaceUserFlows`, `TelegramExperience`) |
| Core feature-synapse files (`CodeFoundry`/`Company`/`TabularData`/`Awesome`) | 4 | 0 |
| Full-suite `dotnet test` wall time | measure | record delta |
