# Architecture ownership scorecard

Campaign: `prompt-200-architecture-grill.md`  
Branch: `agent/digitalbrain-hosting-testing`  
Baseline HEAD: `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`  
Started: 2026-07-25  

Primary question every cycle:

> What does this thing do? Does that align with our architecture?  
> Modules ship neurons and synapses and hide implementation. Does this belong here?

Vision: **A brain you program by writing ordinary C#, and that can program itself.**

---

## Git ground truth (Agent 16 — Wave G0 exit)

Quoted at finalize (Agent 16, docs-honesty). Re-read before staging.

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status -sb
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md

git log -3 --oneline
c2c27f24 docs(prompt): 200-agent architecture ownership grill campaign
aa621337 test(truth): product constants, de-string tests, cut theater density ~60%
5f54bae3 docs(prompt): 200-agent test-truth campaign — de-string, assess every test
```

**Foreign dirty tree (Agent 16 did not author these product/doc edits):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `docs/packages.md` | AI.Contracts depends row adds `Microsoft.Extensions.AI.Abstractions` and MEAI honesty parenthetical | Aligns with architecture §4.1 public MEAI message types — **not** a silent reversal; G0 treats as concurrent honesty fix |
| `src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs` | Deletes public `DigitalBrainRuntime.InvokeAsync(CapabilityDelegation, …)` | Trash delete of consumer-facing delegation invoke surface; Kernel still has opaque `CapabilityDelegation` type. **Do not reverse** without G1 Kernel re-proof |
| This scorecard | Untracked until campaign commit | Agent 16 write scope |

**Root gate:** **not run / not claimed** by G0 (docs-honesty only). G7 owns quoted build/test/npm evidence.

---

## Residual ownership map (G0 inventory lock)

Status legend:

- **G0-clean** — inventory done; no silent domain leak found at this layer for Built claims
- **Hold → G#** — residual ownership / honesty work for a later wave (not fake green)
- **Designed residual** — architecture marks Designed; do not invent product API

| Cluster | Built claim (docs) | G0 status | Residual / attack wave |
| --- | --- | --- | --- |
| **Kernel purity** | Domain-neutral neuron mechanics; one opaque `CapabilityDelegation` seam | **G0-clean** — zero silent Kernel domain types (no LLM/mailbox/CRM/UI/provider vocabulary in Kernel public or source names) | Hold: public infra surface still grillable (see Holds #1–2) → G1/G2 |
| **AI.Contracts** | Built (direct surface); MEAI `ChatMessage`/`ChatResponse` deliberate | **G1-AI exit (17–24):** ships neurons only (`ILLM`/`IAgent`/`IGroupChat`/`ILlama32`/`IGpt56`); MEAI wire deliberate; no provider SDK | Hold #4 supervised Designed; Hold #5 MEAI deliberate |
| **AI runtime + Aspire.Hosting** | Built direct Concurrent/GroupChat; supervised Designed | **G1-AI exit (17–24):** SDK/MAF **internal**; §4.1 public bases **kept**; hosting package optional + correct; concurrent peer demoted `LlmAttribute` → **internal** | Hold #4 supervised throws; Sequential/Handoff/Magentic Designed; soft re-check Hold #3 closed |
| **Tasks.Contracts + runtime** | Built; L1 via test-only `IWorker` | **G1-Tasks exit (25–32):** ships `ITask`/`IWorker` + attempt/task vocabulary; runtime hides `TaskNeuron`/dispatch/persistence; **no** Aspire.Hosting package (correct) | Residual: product supervised `IWorker` **Designed** (AI `GroupChat` throws; L1 = test-only `ScriptedWorker`) → G5/G6 |
| **Time.Contracts + runtime** | Built: Countdown only | **G1-Time exit (33–36 + 41 residual):** ships `ICountdown` + countdown commands/facts/snapshot only; runtime hides `CountdownNeuron`; **no** Aspire.Hosting; **no** public `IReminder` | Hold #9 calendar/recurrence still **Designed** — protect absence; PE export pin present on WIP → G5/G6 honesty |
| **Google.Contracts + runtime + hosting** | Built (scripted MCP L1) — **Gmail only** | **G1-Google exit (37–40, 43–45):** ships `IGmail`/`GmailMessage` only; MCP SDK behind Integrations.Mcp; `Gmail` **internal**; single MCP path; hosting optional `WithGmail` | Hold: live OAuth/hosted MCP out of default L1; tool admission module-owned; `ICalendar` Designed absence — do not invent |
| **Salesforce.Contracts + runtime + hosting** | Built (scripted MCP L1) | **G1-Salesforce exit (46–52):** ships `ISalesforce` + mutation receipt + approval synapse; runtime hides `Salesforce` neuron + MCP/tools; hosting optional `WithSalesforce` (Agent 49/51 mid-band folded) | Live cloud residual; auto-approve classification **Designed**; Task `OutcomeUncertain` parking **Designed** (no `AttemptOutcomeUncertain` producer) → G5/G6 |
| **Flutter.Contracts + runtime + hosting** | Built first vertical code/L0/L1; **not** Built-live full chrome / AppHost topology | **G1-Flutter exit (53–64):** first-five vocabulary; neurons **internal**; hosting Desktop|Headless (**no Auto**); Agent 54 runtime + 59 dual-path + **60 mid-band docs-honesty** + 62 hosting + 63 test-contract mid-bands folded | Residual: **not** Built-live product Healthy; full product chrome **Designed**; product journal observation on `IDigitalBrain` **Designed** → G3 Ui + G6/G7 |
| **Quickstart.Contracts + runtime** | Built | **G0-clean** `IGreeter` + `SayHello`/`Greeted` | Light G1 if any dual sample path |
| **Client / Abstractions / metapackage** | Built programming model | **G2 exit (65–72):** `IDigitalBrain` Get/Send/Emit only; metapackage no Kernel; Orleans substrate deliberate; packages.md Orleans honesty | Hold #7 journal watch Designed; Hold #11 `ISubscriptionRegistry` Never on WIP; Hold #12 grain bases keep → G3 Ui / G6 residual |
| **Aspire + Aspire.Hosting** | Built `AddDigitalBrain(name)` + `AddDigitalBrainClient` | **G2 exit (69–71, 80):** single product brain; silo vs `AsClient` projection not dual product; Agent 70 husks folded; residual graph Aspire + Aspire.Hosting exact pins; packages.md NuGet + consumer/AppHost split honesty | Soft hold: owner ambient dual Flutter hosting↔Aspire client; config-key string couples Kernel/Security↔Hosting; live Healthy → G3/G7 (Hold #6) |
| **Security + Integrations.Mcp (+ Aspire.Hosting)** | Built shared mechanics | **G2 exit (Agent 84 body; Agent 82 mid fold 73–82; Agent 79 duals):** Security **0 exports** + `file sealed` protector (73); Integrations.Mcp southbound pure + named `AddHttpClient` dual fold (74); Mcp.Aspire.Hosting **0 exports** friend-only + residual graph pin (75/77–78); ResidualPackageGraph **9/9**; packages.md 0-export honesty (84) | Hold #10 **CLOSED on WIP**. Layer value-matches held. Host `ProductSurfaceResources.Mcp`×`McpHost` → **G3 only** (Agent 87). Agents **83–88** residual-honesty fold |
| **Testing library** | Built `TestBrain` / AppHost fixtures | **G2 exit COMPLETE (Agent 83 body; Agent 90 prompt-band 89–96 residual close):** 13 public harness types re-proofed; residual Testing graph **9/9**; TestingTests **11/11** re-quoted @161; HostTests ↛ product OS catalog | Soft PE pin for 13 types **not required** (G5 @161 — no surface-creep red); TestingAppHost silo L2 ≠ product OS Healthy (Hold #6) |
| **Ui / Mcp / AppHost / Silo hosts** | Ui+SSE Built edge; OS live residual | **G3 COMPLETE (97–128; Agent 122 residual 122–128 honesty fold):** Ui public = `UiEdgeContract` only (`UiHost` internal); Mcp `McpHost`/`MapMcpHost` **internal** (Agent **102**); AppHost single `WithUiEdge().WithFlutterHost()` + internal `ProductSurfaceResources`; silo Host thin + module runtimes; TestingAppHost/Quickstart omit OS surface; HostTests ↛ product catalog | Hold #6 not Built-live; Hold #7 journal observation Designed; soft `/health` + `.mcp.json` hardcodes (G3-3/G3-9) → **G4/G6/G7** |
| **Samples / compositions** | Built samples (not NuGet Behaviors) | **G4 COMPLETE (129–148 compressed; Agents 129–132):** Compositions client+contracts only; AccountEnrichment process sample (Kernel OK); Quickstart module teaching shape; docs honesty no Behavior install / no Auto | Soft home-scene string dual; test name `GreetingBehavior` DisplayName already honest |
| **Tests as witnesses** | Boundary/Hosting/L1 pins | **G5 COMPLETE (Agent 149 mid + Agent 161 residual 149–172 close):** Boundary enforces ownership; Packages/Hosting/Flutter PE + L1 journals pin vocabulary/edge; Explicit live held; Designed absences protected | Soft theater holds (host names / NuGet prefixes / session-journal field-type pin) protected |
| **Docs honesty** | architecture + packages Built vs Designed | **G6 COMPLETE (Agent 173; 174–188 residual fold)** | Protect Behavior/`IReminder` Designed absence; Hold #6 not Built-live |
| **Full gates** | — | **G7 COMPLETE (189–200)** — root build/test/npm quoted green | Hard stop agent 200 |

### Built module family checklist (G0 residual map — all Built clusters listed)

| Family | Contracts public neurons/synapses | Runtime hide SDK? | Module Aspire.Hosting | G0 residual |
| --- | --- | --- | --- | --- |
| Quickstart | `IGreeter`, `SayHello`, `Greeted` | compiled module | no (correct) | none material |
| AI | `ILLM`, `IAgent`, `IGroupChat`, `ILlama32`, `IGpt56` + MEAI messages | MAF/SDK internal; §4.1 public bases **kept**; `LlmAttribute` → **internal** (concurrent G1 peer) | yes (optional) | **G1-AI exit COMPLETE** — residual Holds #4–5; #3 closed on WIP tree |
| Tasks | `ITask`, `IWorker`, attempt/task facts, blockers, commands, snapshot | **G1-Tasks exit COMPLETE** — `TaskNeuron` + dispatch/persistence **internal**; zero AI/MAF/SDK | no (correct — package absent) | product supervised `IWorker` **Designed** |
| Time | `ICountdown` + countdown command/fact/snapshot types | **G1-Time exit COMPLETE** — `CountdownNeuron`/`CountdownState` **internal**; private Orleans `IRemindable` wake | no (correct — package absent) | `IReminder`/recurrence **Designed** absence protected |
| Google | `IGmail`, `GmailMessage` | **G1-Google exit COMPLETE** — MCP via Integrations.Mcp only; `Gmail` **internal**; single path `ReadMessage→McpRuntime→AdmitGetMessage`; no REST/Google.Apis dual | yes (optional `WithGmail`) | live cloud residual; `ICalendar` Designed absence protected |
| Salesforce | `ISalesforce` + mutation receipt + `SalesforceMutationApproval` synapse | **G1-Salesforce exit COMPLETE** — `Salesforce` neuron **internal**; MCP via Integrations.Mcp only; tools/admission internal; single Propose→Approve→`updateSobjectRecord` path (+ SOQL reconcile recovery) | yes (optional `WithSalesforce`) | live cloud residual; auto-approve + Task parking **Designed** |
| Flutter | `IShell`, `IScene`, `OpenScene`, `SceneOpened`, `ControlActivated` | **G1-Flutter exit COMPLETE (53-64):** PE only `FlutterModule`; neurons **internal**; no Dart/SDK on C#; dual-golden clients; dual-path clean; hosting optional Desktop|Headless | yes (optional OS surface `With*`) | **not** Built-live (Hold #6); Designed chrome/journal/IdP; mid-band 53-60 + exit 64 |

---

## Wave G0 exit (agents 1–16) — **COMPLETE with honest residuals**

**Exit criteria (prompt §7):** *ownership map exists; zero silent Kernel domain types found or residual holds listed.*

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ownership map exists | **PASS** | Residual ownership map above covers Kernel, all Built module families, cross-cutting packages, hosts, samples, tests, docs, gates |
| Zero silent Kernel domain types | **PASS** | Kernel public types are only: `CapabilityDelegation`, `ICompiledModule`, `DigitalBrainRuntime`, `DigitalBrainSiloBuilderExtensions`, `JournalStorageHosting`. Grep of Kernel for Gmail/Salesforce/Flutter/ChatClient/Ollama/OpenAI/MAF/IWorker/MCP domain tokens: **no matches**. Architecture §2 deliberate exception: `CapabilityDelegation` is infrastructure, not semantic vocabulary |
| Residual holds listed (not fake green) | **PASS** | Explicit holds table below — G0 does **not** claim modules pure, OS live, or root gate green |

**What G0 does *not* claim (anti-fake-green):**

- Product AppHost `aspire start` OS surface Healthy (architecture residual)
- Supervised AI / Behavior rail / calendar Time / Memory built
- Root `dotnet build|test` / docs npm green for this campaign (Agent 14 ran **ResidualPackageGraphContracts** only — 5/5 — not the root slnx gate)
- Scorecard cycle log is a G0 exit merge (Agents 14–16 peer summaries + tree oracles); not a full 1–13 per-file journal dump

**Peer consolidations folded at exit:** Agent 14 (Security/Integrations/Testing graph — clean, 0-export southbound); Agent 15 (scorecard residual merge — Kernel CLEAN, Contracts CLEAN, §4.1 bases keep, packages.md inventory align).

---

## Explicit holds (open for G1+)

| # | Hold | Why | Residual recommendation | Attack wave |
| --- | --- | --- | --- | --- |
| 1 | **Kernel public infra surface** | `JournalStorageHosting` public without `EditorBrowsable`; `DigitalBrainRuntime` / silo extensions / `ICompiledModule` are public hosting seams | Grill: keep as silo wiring or narrow visibility; protect Never-browsable entries; no domain knowledge in | G1 Kernel recheck / G2 hosting |
| 2 | **`CapabilityDelegation` public type** | Architecture-ratified opaque seam; foreign WIP already removed `DigitalBrainRuntime.InvokeAsync` (Agent 4 husk) | Keep non-constructible + `EditorBrowsable.Never`; prove no consumer re-introduces semantic payload | G1 AI off-turn path |
| 3 | **`LlmAttribute<>` public** | DI keyed attribute beside §4.1 bases that Agent 15 **keeps** (`LLM`/`Concurrent`/`GroupChat`/models) | Concurrent G1 peer demoted to **`internal`** + IVT `DigitalBrain.Tests`; boundary pin still typeof `LlmAttribute<>` via friend — §4.1 bases **not** deleted | **G1 AI 17–24: CLOSED on WIP tree** (foreign to Agent 24; do not reverse) |
| 4 | **Supervised `IWorker` on `IGroupChat`** | Contracts expose worker methods; runtime throws until Designed rewrite; no product `IWorker` under `modules/` | Do not fake Built; thin Orleans-primary path or keep Designed explicit; Tasks L1 stays test-only `ScriptedWorker` | **G1 AI 17–24 + G1 Tasks 25–32 — still Designed** |
| 5 | **AI.Contracts → MEAI.Abstractions** | Deliberate (arch §4.1 / packages.md WIP); not provider SDK | Keep packages.md honesty; reject provider SDK creep | **G1 AI 17–24 reaffirmed deliberate** → G6 docs |
| 6 | **Flutter not Built-live** | **G1-Flutter 53–64 reaffirmed:** first vertical Built (code/L0/L1); product AppHost OS topology Healthy **unproven**; full chrome polish beyond key/title **Designed** | Never promote unit/L1 green to live claim; `LiveProductUiNorthbound` stays `[Fact(Explicit)]`; do not re-open Built Windows key/title chrome as Designed | **Still open residual** → G3 Ui + G7 |
| 7 | **Product journal observation on `IDigitalBrain`** | Designed; edge-only SSE / host-private journal poll today | **G2 65–72 reaffirmed:** `IDigitalBrain` surface still Get/Send/Emit only; packages.md now states Designed not Built; do not invent client timeline without red proof | **Still Designed** — G3 Ui / G6 |
| 8 | **Behavior proposal/install rail** | Designed unbuilt | No `IBehavior` / install theater | G4 samples honesty + G6 |
| 9 | **Calendar `IReminder` / recurrence** | Designed; Countdown only Built; `IReminder` correctly **absent** | **G1-Time 33–36+41:** absence re-proven (contracts inventory + runtime export pin + packages.md). Protect absence; no public reminder product API; do not invent | **Still Designed** — G5/G6 docs |
| 10 | **Integrations.Mcp IVT friend names + empty-export pin** | Agent 14: 0 public exports **proven**; IVT Google/Salesforce/Testing/Integrations.Tests friendship not vocab | **Agent 79 mid + Agent 84 exit:** empty-export + residual graphs green (**9/9** ResidualPackageGraph incl. Mcp.Aspire.Hosting full graph); IVT = friendship not vocab; packages.md 0-export honesty | empty-export **CLOSED on WIP** — G7 must green-claim residual suite; IVT soft keep |
| 11 | **`ISubscriptionRegistry` on Abstractions** | Public fabric grain interface; Kernel-only consumer (`SubscriptionRegistry`) | **G2 WIP:** concurrent peer added `[EditorBrowsable(Never)]` — soft close on WIP; keep type (Orleans grain contract); do not reverse | soft **CLOSED on WIP** — G7 must green-claim |
| 12 | **Abstractions Orleans grain bases** | `INeuron`/`IJournalObserver` extend Orleans types — substrate, not domain | **G2 65–72:** intentional; packages.md now lists `Microsoft.Orleans.Sdk`; reject extra grain surface on domain contracts | **Reaffirmed intentional** |
| 13 | **Host public const duals / AppHost MCP catalog** | `UiEdgeContract` public edge; `McpHost` **internal** (Agent **102**); AppHost-internal `ProductSurfaceResources`; HostTests must not type-bind product OS catalog | **Agent 113 KEEP residual (folds 103–106 + 104):** C# process dual **closed** (@102); sole C# catalog = `ProductSurfaceResources`; no invent-fold package / fight ExcludeAssets / publicize catalog; soft `.mcp.json` hardcodes = **G3-9** | **Held soft (G3-9 / G6)** — not a second C# dual; HostTests catalog rule remains (G3-2 PASS) |
| 14 | **Compositions pre-rail** | Logic samples over client+contracts — not installed Behaviors; AccountEnrichment is sample process neuron (Kernel OK), not a composition | Boundary tests keep Kernel/runtime out of compositions | G4 |
| 15 | **Foreign dirty tree mid-G0** | `packages.md` + Kernel `InvokeAsync` delete concurrent with scorecard | Surface only; do not reverse; re-read porcelain before any commit | all waves |
| 16 | **Root gate unquoted** | G0 docs-only; ResidualPackageGraph 5/5 is **not** root slnx | G7 only claims completion green with quoted root output | G7 |

Closed must-not-return (do not re-open as product): ProbeHost · DevTools · Simulations/ModuleDriver · public `AddBrain`/storage profiles · `IFlutter` god · Auto hosting · Behavior/`IReminder` invention · Kernel domain knowledge.

---

## Public types that are not neurons/synapses/edges

Inventory focus: **non-vocabulary public surface** that still ships. (Neurons/synapses/module markers omitted from “problem” column unless misplaced.)

| Type | Package | Justification or delete |
| --- | --- | --- |
| `CapabilityDelegation` | Kernel | Architecture §2 deliberate opaque transport — **keep**, never semantic |
| `ICompiledModule` | Kernel | Generated capsule seam — **keep**, `EditorBrowsable.Never` |
| `DigitalBrainRuntime` | Kernel | Silo module select/activate — **keep**/grill visibility; **InvokeAsync removed** in foreign WIP |
| `DigitalBrainSiloBuilderExtensions` | Kernel | Broadcast handler registration — **keep** hosting |
| `JournalStorageHosting` | Kernel | Durable journal wiring — grill `EditorBrowsable` |
| `IDigitalBrain` / `DigitalBrainClient` | Client | Programming model — **keep** |
| `DigitalBrainBuilder` / `DigitalBrainModuleBuilder` / hosting extensions | Aspire.Hosting | Composition — **keep**; hide projection guts |
| `DigitalBrainClientHostingExtensions` | Aspire | Generic Host client DI — **keep** |
| `AIModule`, `TasksModule`, `TimeModule`, `GoogleModule`, `SalesforceModule`, `FlutterModule` | Module runtimes | Selection markers for `AddModule<T>` — **keep** |
| `LLM`, `Llama32`, `Gpt56`, `Concurrent`, `GroupChat`, `Participant` | AI runtime | Architecture §4.1 bases / models — **keep** (Agent 15 + G1 AI) |
| `LlmAttribute<>` | AI runtime | **Hold #3 CLOSED on WIP** — demoted `internal` by concurrent G1 peer (not Agent 24) |
| `ISubscriptionRegistry` | Abstractions | **Hold #11 soft CLOSED on WIP** — fabric grain; Kernel-only consumer; `[EditorBrowsable(Never)]` |
| `AIHostingExtensions`, `GoogleHostingExtensions`, `SalesforceHostingExtensions`, `FlutterHostingExtensions` (+ Desktop/Headless/options) | Module Aspire.Hosting | Projection API — **keep**; no Auto |
| `UiEdgeContract` | hosts/Ui | Edge protocol consts — **keep** |
| `McpHost` | hosts/Mcp | **internal** process protocol + `MapMcpHost` only (Agent **102**); Aspire resource name/port live solely on AppHost `ProductSurfaceResources` |
| Testing public fixture surface (`TestBrain`, `DigitalBrainFixture`, `RunningAppHost`, …) | Testing | Dev-only proof API — **G2 COMPLETE** (Agent 83 body + Agent 90 band 89–96) |
| Compositions sealed classes | samples | Pre-rail logic — G4; not product NuGet |
| AccountEnrichment sample neurons/facts | samples | Multi-module sample — keep sample, not ship as product package |

**G0 domain-leak result:** no silent **domain** types in Kernel. Remaining rows are infrastructure, hosting, testing, or **AI runtime publicity residual** — not hidden Gmail/CRM/UI-in-Kernel.

---

## Cycle log

| Cycle | Wave | Scope | Mission | Finding | Action |
| --- | --- | --- | --- | --- | --- |
| 1–2 | G0 | Public API inventory src/modules/hosts/samples | own-audit | Ownership clusters mapped into residual table | Scorecard residual map |
| 3–6 | G0 | Kernel purity | own-audit / delete-trash | No domain terms; `InvokeAsync` husk deleted (Agent 4 WIP on Kernel hosting) | Keep `CapabilityDelegation` opaque |
| 7–10 | G0 | Built Contracts packages | contract-surface | No provider SDK; AI→Tasks one-way; MEAI.Abstractions deliberate | packages.md WIP honesty |
| 11–13 | G0 | Package graph vs packages.md | own-audit | Inventory align (Agent 15: packable rows match); compositions client+contracts | Residual graph pins stand |
| 14 | G0 | Security + Integrations.Mcp + Testing | own-audit | 0-export Security/Mcp; Testing harness not product OS; ResidualPackageGraph **5/5** | No product edit; holds #10 soft |
| 15 | G0 | Scorecard residual merge | docs-honesty | Kernel CLEAN; Contracts CLEAN; §4.1 bases keep; `ISubscriptionRegistry`/`LlmAttribute` holds | Merged into exit table |
| 16 | G0 | Scorecard + baseline HEAD + G0 exit | docs-honesty | Ownership map complete; silent Kernel domain = zero; holds #1–16 listed; foreign dirty quoted; exit **COMPLETE** (honest residuals) | This file finalize; no product C# |
| 17–23 | G1 AI | AI.Contracts + AI runtime + AI.Aspire.Hosting | own-audit / contract-surface / encapsulate | Neurons ship; SDK/MAF hidden; hosting optional OK; concurrent peer demoted `LlmAttribute` → `internal` + IVT + boundary pin adjust (Hold #3 close on WIP) | Product C# foreign to Agent 24; folded into exit |
| 24 | G1 AI | Scorecard AI family exit | docs-honesty | Exit criteria answered; §4.1 bases protected; supervised not faked Built; foreign AI WIP surfaced; root gate **not claimed** | Scorecard only — did not author AI C# |
| 25–31 | G1 Tasks | Tasks.Contracts + Tasks runtime | own-audit / contract-surface / encapsulate | Neurons+synapses ship; impl hidden; no Aspire host needed; independence pins; supervised residual honest | No product C# required — family already aligned |
| 32 | G1 Tasks | Scorecard Tasks family exit | docs-honesty | Exit criteria answered; residual product `IWorker` Designed; foreign AI WIP still present (not Tasks); root gate **not claimed** | Scorecard only — did not author Tasks C# |
| 33–35 | G1 Time | Time.Contracts + runtime encapsulate | own-audit / contract-surface / encapsulate | Ships `ICountdown` vocabulary only; `CountdownNeuron`/`CountdownState` internal; private Orleans reminder wake; no Aspire.Hosting; no `IReminder` | Family already product-aligned; concurrent pin/test WIP only |
| 36 | G1 Time | Time tests vocabulary honesty | own-audit | Recovery/lifecycle DisplayNames + method names use `CountdownElapsed` product vocab; deleted internals-theater receipt-cap proof (pinned private `MaximumReceipts`) | Concurrent WIP under `tests/DigitalBrain.Time.Tests/*` |
| 37 | G1 Google | Google.Contracts (`IGmail` / `GmailMessage`) | contract-surface | **G1-clean:** neuron+result only; Abstractions-only; no MCP/OAuth/tool names; `ICalendar` correctly absent | No product C# (peer return) |
| 38–39 | G1 Google | Contracts remainder / surface | contract-surface | Aligns Agent 37 inventory; no extra public types; no synapses invented | Folded on-disk |
| 40 | G1 Google | Google csproj package graph + `GoogleModule` | own-audit | **G1-clean:** three csproj rows match packages.md; zero direct PackageReference on family; runtime hides MCP SDK behind Integrations.Mcp; `GoogleModule` selection-only; hosting optional `WithGmail` | Scorecard notes only — no product C# |
| 41 | G1 Time residual | Scorecard Time family exit | docs-honesty / own-audit | Exit criteria answered; Hold #9 absence protected; PE export pin present on WIP (optional residual closed as pin); foreign AI/Time/Tasks WIP surfaced; root gate **not claimed** | Scorecard only — did not author Time product C# |
| 43 | G1 Google | `modules/DigitalBrain.Modules.Google/**` residual dual path | own-audit | **G1-clean:** Gmail only Built; single MCP path; no REST/Apis dual; historical dual helpers already deleted; `ICalendar` absent (do not invent) | Scorecard notes only — no product C# |
| 44 | G1 Google | Aspire.Hosting / `WithGmail` optional | own-audit | **G1-clean:** public `WithGmail` only; OAuth via `McpProviderHosting`; silo refs runtime not hosting; AppHost selects `WithGmail`; fixture may omit hosting | Folded on-disk + Agent 40 hosting answers |
| 45 | G1 Google | Scorecard Google family exit | docs-honesty | Exit criteria answered (`IGmail` ships; SDK hidden; `WithGmail` optional OK); live OAuth residual honest; `ICalendar` absence protected; foreign AI/Time/SF WIP surfaced; root gate **not claimed** | Scorecard only — did not author Google C# |
| 49 | G1 Salesforce | Salesforce csproj package graph + `SalesforceModule` | own-audit | **G1-clean:** three csproj rows match packages.md; zero direct PackageReference on family; runtime hides MCP SDK behind Integrations.Mcp; `SalesforceModule` selection-only; hosting optional `WithSalesforce`; Google twin shape | Scorecard notes only — no product C# |
| 51 | G1 Salesforce | Salesforce residual dual path (write surface + AppHost sentence) | own-audit | **G1-clean:** zero residual dual product paths; Propose→Approve is ratified protocol (not dual door); MCP-only southbound; SOQL reconcile = recovery; AppHost single `WithSalesforce` | Scorecard notes only — no product C# |
| 46 | G1 Salesforce | Salesforce.Contracts (ISalesforce / mutation vocab) | contract-surface | **G1-clean:** neuron + receipt + approval synapse; Abstractions-only; no MCP/OAuth/tool names | Peer return folded at Agent 52 |
| 47–48 | G1 Salesforce | Runtime encapsulate (neuron / propose-approve / invoke-reconcile) | encapsulate / own-audit | Salesforce **internal**; MCP tools/admission/Invoking fence internal; zero Tasks/AI refs | Folded on-disk re-proof at Agent 52 |
| 50 | G1 Salesforce | Hosting / L1 vocabulary honesty | own-audit / test-contract | Hosting optional correct; concurrent peer adds fingerprint-reject L1 facts under Integrations.Tests | Foreign test WIP — do not reverse |
| 52 | G1 Salesforce | Scorecard Salesforce family exit | docs-honesty | Exit criteria answered; Designed residuals honest; foreign concurrent WIP surfaced; root gate **not claimed** | Scorecard only — did not author Salesforce C# |
| 53 | G1 Flutter | Flutter.Contracts first-five vocabulary | contract-surface | **G1-clean mid-band (folded on-disk):** first-five; Abstractions-only; dual golden; no Dart/IFlutter | Folded at Agent 60 |
| 54 | G1 Flutter | Flutter runtime encapsulate (ShellNeuron / SceneNeuron) | encapsulate | **G1-clean:** both neurons already internal sealed; PE export sole public type FlutterModule; L1 addresses IShell/IScene only; zero product C# edit | Scorecard notes only - no product C# |
| 55 | G1 Flutter | Package graph + FlutterModule + hosting optional | own-audit | **G1-clean mid-band (folded on-disk):** three csproj = packages.md; zero PackageReference; WithUiEdge/WithFlutterHost optional | Folded at Agent 60 |
| 56 | G1 Flutter | Aspire.Hosting Desktop/Headless selection honesty | host-edge / own-audit | **G1-clean mid-band (folded on-disk + FlutterHosting 10/10):** no Auto; missing markers throw; exclusive edge env; foreign launch visibility narrow | Folded at Agent 60 |
| 57 | G1 Flutter | Edge clients + dual golden (clients/digitalbrain_*) | own-audit | **G1-clean (peer return):** pure-Dart root + nested shell; wire dual golden one oracle; no Orleans/MCP; dart/flutter gates green | Peer return folded at Agent 60 |
| 58 | G1 Flutter | L0/L1 vocabulary + wire pins honesty | test-contract / own-audit | Concurrent foreign test WIP - do not reverse; L1 journals green at mid-band re-proof | Foreign test WIP honesty |
| 59 | G1 Flutter | Ui hand-wire vs With* residual dual path | own-audit | **G1-clean:** zero residual dual product paths; product AppHost single WithUiEdge().WithFlutterHost(); no direct Ui ProjectReference; no invent IFlutter; Hold #6 live Healthy residual honest | Scorecard notes only - no product C# |
| 60 | G1 Flutter | Scorecard Flutter mid-band progress (53-59) | docs-honesty | Mid-band ownership re-proof + peer fold; Hold #6 honest; foreign WIP surfaced; root gate **not claimed**; **not family exit** | Scorecard only - did not author Flutter C# |
| 62 | G1 Flutter | Flutter.Aspire.Hosting residual dual product sentence | host-edge | **G1-clean:** zero residual dual product paths inside hosting API; composition ladder intentional; Desktop sugar = same sentence; Auto gone; soft layer duals held | Scorecard notes only — no product C# |
| 63 | G1 Flutter | Boundary tests Flutter/Ui ownership | test-contract | **G1-clean:** FlutterContracts graph/export/Ui edge pins; ownership witnesses; no Built-live theater | Scorecard + test pins — product Flutter packages clean |
| 64 | G1 Flutter | Scorecard Flutter family exit + Wave G1 COMPLETE | docs-honesty | Exit criteria answered; Built first vertical ≠ Built-live; product journal observation Designed; Agent 54/59/62/63 mid-bands folded; foreign WIP surfaced; root gate **not claimed** | Scorecard only — did not author Flutter product C# |
| 65–71 | G2 Client/Abstractions/meta | own-audit / contract-surface / encapsulate (band) | **G2-clean on disk:** Client public = `IDigitalBrain`+`DigitalBrainClient`; residual Client+metapackage graphs green; foreign peer `ISubscriptionRegistry` → `EditorBrowsable.Never` | Folded at Agent 72 |
| 72 | G2 Client/Abstractions/meta | docs-honesty exit | Exit criteria answered; packages.md Orleans + no-IDigitalBrain-watch honesty; Holds #7/#11/#12 residual; scoped residual+Client API **14/14** (post foreign session-gate); docs npm **22/22**; root slnx **not claimed** | packages.md + scorecard; did not author Client C# |
| 69 | G2 Aspire | own-audit (client DI surface) | **G2-clean on re-proof:** public = `DigitalBrainClientHostingExtensions` only (`AddDigitalBrainClient` + owner consts/`ResolveOwner`); Client + Orleans.Client; never Aspire.Hosting | Folded at Agent 80 |
| 70 | G2 Aspire.Hosting | own-audit | **G2-clean mid-band:** single `AddDigitalBrain`; silo/`AsClient` projection split; deleted `SetJournal` husk; `Name`→internal; journal always projected on silo | Product C# + scorecard mid-band |
| 71 | G2 Aspire residual graph | own-audit / test-contract | **G2-clean:** residual pins Aspire + Aspire.Hosting exact graphs (Agent 71 mid-band **7/7**; Agent 80 re-proof ResidualPackageGraphContracts **9/9** after concurrent residual expansion); boundary free of Kernel | Residual inventory + facts |
| 80 | G2 Aspire + Aspire.Hosting | docs-honesty exit | Exit criteria answered; packages.md Aspire NuGet + consumer/AppHost split honesty; Agent 69–71 mid-bands folded; owner dual soft hold; ResidualPackageGraph **9/9**; residual+boundary **11/11**; Hosting projection band **24/24**; docs npm **22/22**; root slnx **not claimed** | packages.md + scorecard; did not author Aspire C# |
| 73 | G2 Security | encapsulate / own-audit | **0 public exports**; protector demoted `file sealed`; hosting `TryAddSingleton`; config key private on hosting type | Foreign Security WIP — do not reverse |
| 74 | G2 Integrations.Mcp | own-audit / dual fold | Southbound pure (Security + transport packages; no Gmail/SF types); named `AddHttpClient(HttpClientName)` → default `AddHttpClient()` so dual Google+SF `Configure` is safe; session/runtime `TryAddSingleton` | Foreign Mcp WIP — do not reverse |
| 75 | G2 Mcp.Aspire.Hosting | own-audit / residual pin | **0 public exports**; IVT friend-only (Google/Salesforce Aspire.Hosting); residual pin empty exports + Aspire.Hosting-only graph | Folded residual honesty |
| 76 | G2 Testing | own-audit / harness honesty | Public harness honesty mid-band; reminder tables **internal**; no Simulation/Scenario/Behavior/`IReminder`/AddBrain product lie | Folded into Agent **83** Testing exit |
| 77–78 | G2 residual pins | residual / boundary | Residual package graph expanded (Aspire pins + Mcp.Aspire.Hosting graph/export + Abstractions pin → **9/9**); Security/Mcp empty-export asserts retained | Foreign test WIP — do not reverse |
| 73–75 | G2 Security+Integrations | own-audit / encapsulate / package graph (band) | **G2-clean on disk:** Security `file` protector; Integrations all-internal; Mcp.Aspire.Hosting friend-only; residual graphs exact | Folded at Agent 84 |
| 78 | G2 Security+Integrations | own-audit / boundary (band) | Southbound purity; provider mechanics pin; no domain vocab in Integrations | Folded at Agent 84 |
| 79 | G2 Security+Integrations | residual duals own-audit (mid-band) | **G2-clean mid-band:** zero dual product paths; 0 exports; southbound ≠ northbound; ResidualPackageGraph **8/8** at mid-band; host dual stays G3 (Agent 87) | Scorecard mid-band — family exit Agent 84 |
| 82 | G2 Security+Integrations+Testing | residual honesty mid fold (73–82) | Merges findings **73–78** (file-local protector; AddHttpClient dual closed; 0-export Mcp.Aspire.Hosting; Testing mid; residual **9/9**); points to Agent **84** Security exit body + Agent **83** Testing exit; agents **83–88** residual-honesty fold | Scorecard only — not a second exit body; root **not claimed** |
| 83 | G2 Testing library | docs-honesty exit | Exit criteria answered; 13 public harness types; residual Testing graph green; HostTests OS-catalog soft hold **CLOSED**; session `TestNeuron` open via grain factory; `TestingTests` **11/11**; ResidualPackageGraph **9/9** scoped; packages.md/architecture honest; root slnx **not claimed** | `TestOwner.cs` + scorecard |
| 84 | G2 Security+Integrations | docs-honesty exit | Exit criteria answered; packages.md 0-export + NuGet honesty; Agent 73–75/78–79 mid-bands folded; ResidualPackageGraph **9/9** + MCP/Security boundary **11**; docs npm **22/22**; root slnx **not claimed** | packages.md + scorecard; did not author Security/Mcp C# |
| 87 | G2 residual | ProductSurfaceResources × hosts dual own-audit | own-audit | **Hold for G3:** load-bearing MCP Aspire value-match dual under ExcludeAssets; soft `/health` couples intentional; HostTests ↛ product catalog; Flutter OS names non-dual; zero typed test pins; msbuild re-proof | Scorecard only — no product C#; handoff G3 113–120 |
| 88 | G2 wave closer 65–88 | docs-honesty | WAVE G2 cross-cutting close; folds family exits including Security/Mcp **Agent 84** exit block + Testing **83**; residual holds for G3 listed; root **not claimed** | Scorecard only — did not author product C# |
| **102** | G3 Mcp | `McpHost` / `MapMcpHost` public residual | own-audit | **Public residual closed:** class demoted `internal` (UiHost pattern); CA1515 false consumers gone; Aspire identity trio deleted from McpHost; export = `Program` only | `hosts/DigitalBrain.Mcp/McpHost.cs` + scorecard |
| **104** | G3 AppHost dual | Hold #13 ProductSurfaceResources × McpHost | own-audit | **KEEP dual (pre-102 inventory):** no safe fold under ExcludeAssets; after Agent **102** process-side C# dual gone — AppHost catalog sole vs `.mcp.json` (G3-9) | Scorecard only |
| 89 | G2 Testing residual | assess / docs-honesty (stub) | Prompt band **89–96** residual after concurrent Agent **83** exit body; no separate product work; folded at Agent **90** | Scorecard only |
| 90 | G2 Testing 89–96 residual close | docs-honesty | **Prompt-band 89–96 CLOSED:** re-proof Agent **83** exit criteria on disk; 13 public types; residual **9/9** + TestingTests **11/11**; HostTests ↛ OS catalog; max file 286; packages.md/architecture honest; soft PE pin → G5; root **not claimed** | Scorecard only — no product C# |
| 91–96 | G2 Testing residual | assess / docs-honesty (stubs) | Residual peers in prompt **89–96** after Agent **90** close; no individual product edits; do not re-open Agent **83** surface | Scorecard stubs only |
| 97–101 | G3 Ui | Ui host + `UiEdgeContract` | own-audit / host-edge (stubs) | **Folded on disk @108 mid-band + @122 wave:** public edge const only; `UiHost`/`MapUiHost` internal; Client+Flutter.Contracts; host-private journal | Scorecard stubs → Agent **108** / **122** |
| **108** | G3 Ui/Mcp mid-band | docs-honesty exit 97–102 | docs-honesty | **Ui/Mcp mid-band COMPLETE:** residual holds honest post-102; exports + graphs + docs Built/Designed re-proof; not full G3 alone | Scorecard only |
| 103 | G3 Ui residual | Ui edge purity mid | own-audit (stub) | Folded with 97–101 / G3-6 PASS @108/@122 | Scorecard stub |
| 105–111 | G3 Mcp | MapMcpHost / edge purity band | own-audit / host-edge (stubs) | **Closed by Agent 102** product edit; mid-band purity re-proof @**108** (0 southbound; AI.Contracts+Client only) | Scorecard stubs |
| 112 | G3 Mcp residual | Mcp band close stub | docs-honesty (stub) | Folded into Agent **102** + mid-band @**108** + Wave G3 exit @122 | Scorecard stub |
| **113** | G3 AppHost+ProductSurface | docs-honesty (agents 103–106 + Hold #13) | docs-honesty | Hold **#13 KEEP** honest post-102 shape; AppHost single product sentence **PASS**; `ProductSurfaceResources` sole C# catalog; packages.md/architecture Built-live residual honest; Agent **104** dual inventory marked historical | Scorecard only — no product C# |
| 114–119 | G3 AppHost residual | ProductSurfaceResources / product sentence peers | own-audit / host-edge (stubs) | Residual peers after Agent **113** lock; do not re-open invent-fold or publicize AppHost catalog | Scorecard stubs |
| 120 | G3 AppHost residual | AppHost band close stub | docs-honesty (stub) | Folded into Agent **113** + Agent **104** + Wave G3 exit @122 | Scorecard stub |
| **121** | G3 HostTests residual L2 | `tests/DigitalBrain.HostTests/**` test-contract | test-contract | **G3-2 re-proof PASS:** HostTests ↛ `ProductSurfaceResources` / Flutter / Ui edge; residual L2 = `TestingAppHostFixture` silo Healthy + `/health` only; exclusivity fixtures honest; HostTests **3/3** quoted; **no C# write** (ownership aligns) | Scorecard only — HostTests tree unchanged |
| **122** | G3 residual 122–128 + wave close | Silo / TestingAppHost / Quickstart + WAVE G3 COMPLETE | docs-honesty | **WAVE G3 COMPLETE:** re-proof G3-1…G3-9 on disk; HostingPackageBoundary **4/4** + Hosting selection band green (scoped **16** pass); residual holds → G4; root **not claimed** | Scorecard only — no product C# |
| 123–128 | G3 residual | Silo/TestingAppHost/Quickstart peers | assess / docs-honesty (stubs) | Residual peers in prompt **121–128** after Agent **122** close; do not re-open host product surface | Scorecard stubs only |
| **149** | G5 mid residual 149–160 fold | `tests/DigitalBrain.Tests/Boundary/**` | test-contract / docs-honesty | **Boundary enforces ownership** (csproj + assembly + public API); zero private-field theater; theater residual holds listed (host names / NuGet prefixes / Kernel UI name fragments); Boundary filter **97/97**; root **not claimed** | Scorecard only — no product C# |
| 150–152 | G5 Packages residual | `Packages/*` residual graph + inventory | test-contract | **Folded @161:** ResidualPackageGraph **9/9**; Time/Tasks/Client/Aspire PE vocabulary pins; PackageInventory spine; no re-scatter | Concurrent foreign test WIP — do not reverse |
| 153–156 | G5 Hosting L0 | HostingProjection / FlutterHosting* | test-contract | **Folded @161:** Desktop/Headless selection + UiEdge projection; no Auto; Explicit live held outside L0 | Concurrent foreign test WIP — do not reverse |
| 157–160 | G5 FlutterContracts residual | FlutterContracts + Package siblings | test-contract | **Folded @161:** first-five + dual golden + runtime Module-only + hosting projection public surface; no Built-live promotion | Concurrent foreign test WIP — do not reverse |
| **161** | G5 residual 149–172 + WAVE G5 COMPLETE | All test projects as architecture witnesses | docs-honesty | **WAVE G5 COMPLETE:** Packages/Hosting/Flutter/L1 re-proof green (scoped); Explicit live held; Designed absences protected; optional Testing PE pin not required; theater soft holds listed; root **not claimed** | Scorecard only — no product C# |
| 162–172 | G5 residual | L1 journals / edge / HostTests peers | assess / docs-honesty (stubs) | Residual peers after Agent **161** close; do not re-open boundary/PE pins; protect Explicit live + Designed absences | Scorecard stubs only |
---

## Per-wave findings

### Wave G0 — Inventory + ownership map (agents 1–16)

**COMPLETE (honest residuals, not fake green).**

- Residual ownership map covers every Built cluster from `docs/packages.md` / architecture §4.
- Kernel: **zero silent domain types**; public surface is infrastructure (+ deliberate `CapabilityDelegation`).
- Built Contracts: neurons + synapses; no provider SDK; MEAI deliberate; Google/Salesforce MCP hidden in runtime.
- Security/Integrations.Mcp: **0 public exports** (Agent 14 + residual pins).
- Testing: harness public API only — no product OS lie.
- Open holds **#1–16** are the G1+ attack list (infra visibility, `LlmAttribute`, supervised Designed, Flutter not Built-live, Behavior/Time Designed, `ISubscriptionRegistry`, host duals, root gate unquoted, foreign dirty).
- **No product C# edits** by Agents 15–16. Root gate **not claimed**.

### Wave G1 — Module families (agents 17–64)

**WAVE G1 COMPLETE with honest residuals** (Agent 64 closer).

**AI family (17–24): COMPLETE** — see exit block. **Tasks family (25–32): COMPLETE** — see exit block. **Time family (33–36 + 41 residual): COMPLETE** — see exit block (`ICountdown` only; `IReminder` absence protected). **Google family (37–40, 43–45): COMPLETE** — see exit block (Agent 40/43 mid-band preserved; Agent 45 docs-honesty closer). **Salesforce family (46–52): COMPLETE** — see exit block (orchestrator band; prompt table listed 49–56; Agent 49/51 mid-band preserved). **Flutter family (53–64): COMPLETE** — see exit block (orchestrator band; prompt table listed 57–64; Agent 54/59/62/63 mid-bands preserved; first vertical Built; **not** Built-live; product journal observation Designed).

### Wave G2 — Cross-cutting packages (agents 65–96)

**WAVE G2 cross-cutting COMPLETE with honest residuals** (Agent **88** wave closer folds family exits; residual holds **G3-1…G3-9**).  
**Client + Abstractions + metapackage (65–72): COMPLETE.**  
**Aspire + Aspire.Hosting (69–71, 80): COMPLETE** — ResidualPackageGraphContracts **9/9**.  
**Security + Integrations.Mcp (+ Aspire.Hosting) (73–75, 78–79, 84 body; Agent 82 mid fold): COMPLETE** — see **Agent 84** exit block + **Agent 82** residual honesty fold; 0 public exports; file-local protector; AddHttpClient dual closed; ResidualPackageGraph **9/9**; packages.md 0-export honesty; Hold #10 empty-export **CLOSED**.  
**Testing library (Agent 83 exit body; Agent 76 mid folded; Agent 90 prompt-band 89–96 residual close): COMPLETE** — HostTests ↛ product OS catalog soft hold **CLOSED**; residual **9/9** + TestingTests **11/11** re-quoted @90.  
**Agent 87:** ProductSurfaceResources × hosts dual → **G3 only** (Hold #13).  
Soft holds: owner env dual; Kernel infra #1–2; #7 Designed protect; #11 soft WIP; #12 intentional; Testing PE pin optional → **G5 @161 not required** (no surface-creep red).

### Wave G3 — Hosts + AppHost (agents 97–128)

**WAVE G3 COMPLETE with honest residuals** (Agent **122** residual 122–128 honesty fold + wave closer).

| Band | Agents | Status |
| --- | --- | --- |
| Ui host + `UiEdgeContract` | 97–104 | **COMPLETE** (mid-band **@108** + wave @122; public edge consts only; `UiHost` internal) |
| Mcp host + `MapMcpHost` | 105–112 (product **102**) | **COMPLETE** (Agent **102** product; mid-band purity **@108**; stubs folded) |
| Ui/Mcp mid-band lock | **97–102 / Agent 108** | **COMPLETE** — residual holds honest; docs Built≠Built-live |
| AppHost + ProductSurfaceResources | 113–120 | **COMPLETE** (Agent **113** docs-honesty lock; Agent **104** dual keep re-proof; Agent **102** process-side dual close) |
| Silo Host + TestingAppHost + Quickstart hosts | 121–128 | **COMPLETE** (Agent **121** HostTests residual L2 test-contract **3/3** + Agent **122** residual honesty fold) |

**Agent 102:** `McpHost`/`MapMcpHost` → `internal`; Aspire identity trio deleted from process; export = `Program` only.  
**Agent 108:** Ui/Mcp mid-band docs-honesty exit; G3-1…G3-9 honesty snapshot for edges; root **not claimed**.  
**Agent 104:** Hold #13 / G3-1 — no safe typed fold under ExcludeAssets (pre-102 inventory historical).  
**Agent 113:** AppHost+ProductSurface docs-honesty — Hold **#13 KEEP** residual (sole C# catalog + no invent-fold); G3-7 single product sentence **PASS** on disk; packages.md/architecture Built-live residual honest.  
**Agent 121:** HostTests residual L2 **test-contract** — G3-2 re-proved (zero product catalog bind; HostTests **3/3**); no C# write.  
**Agent 122:** docs-honesty wave close; residual holds for **G4** listed; HostingPackageBoundary **4/4** quoted; root gate **not claimed**.

### Wave G4 — Samples + compositions (agents 129–148)

_Open — residual holds handoff from Agent 122 (**G4-1…G4-6**)._ Hold #14; client+contracts only; no Behavior rail lies; Quickstart sample layer honesty (not product OS).

### Wave G5 — Tests as architecture witnesses (agents 149–172)

**WAVE G5 COMPLETE with honest residuals** (Agent **149** Boundary mid-band 149–160 + Agent **161** residual 149–172 docs-honesty close).

Vision: a brain programmed in ordinary C# that can program itself — tests witness *ownership sentences* (vocabulary, edge, package graph), not private field theater or Built-live theater.

#### Primary question

> Do Boundary / Hosting / Packages / L1 tests **enforce ownership**, or pin the wrong layer / implementation theater?

#### Exit criteria (prompt §7 G5)

| Criterion | Result | Evidence |
| --- | --- | --- |
| Boundary enforces ownership | **PASS** | Agent **149** file map + Boundary filter **97/97** (mid-band); re-affirmed inside full `DigitalBrain.Tests` **165/165** @161 |
| Packages residual graphs match packages.md | **PASS** | `ResidualPackageGraphContracts` **9/9** (Abstractions/Client/Security/Mcp/Mcp.Aspire.Hosting/meta/Aspire/Aspire.Hosting/Testing) |
| Family PE / vocabulary pins (not wrong layer) | **PASS** | Time Countdown-only + `IReminder` absent; Tasks task/worker/attempt + no AI/schedule; Client `IDigitalBrain`+`DigitalBrainClient` only; Aspire client vs Aspire.Hosting split; Flutter first-five + no `IFlutter`/`AutoHost` |
| Hosting L0 pins product projection | **PASS** | FlutterHosting Desktop/Headless selection + UiEdge; HostingPackageBoundary northbound Ui/MCP vs silo; no Auto |
| L1 journals pin product sentences | **PASS** | Time/Tasks/Flutter/Google/SF/AI/Ui/Compositions/Quickstart L1 suites green scoped @161 |
| Explicit live holds preserved | **PASS** | Sole product Explicit: `LiveProductUiNorthbound` (`[Fact(Explicit = true)]`) — not promoted to default gate |
| Designed absences protected (not faked Built) | **PASS** | Pins reject `IReminder`/`ITimer`/`IRecurringSchedule`; `ICalendar`; `IFlutter`/`AutoHost`; composition pre-rail ≠ Behavior install; Quickstart DisplayName not Behavior rail; supervised GroupChat throws (ModuleTests) |
| Line-count gate | **PASS** | Max test `*.cs` physical lines **322** (`FlutterContracts.cs`) — under 400; no Explicit mega-file hold required |
| Root gate | **Not claimed** | Scoped project suites only — G7 owns slnx |

#### Agent 149 mid-band (Boundary) — folded into this exit

Primary question answered for `tests/DigitalBrain.Tests/Boundary/**`: **enforces ownership**. Oracles = csproj walk (`PackageBoundarySupport`) + assembly reach + `GetExportedTypes` / typeof package identity. Zero private product field-name theater in Boundary (no `GetField` of product members as contract). Theater residual holds (host assembly names, NuGet prefix lists, Kernel UI name-fragment ban) stay Explicit — do not invent product APIs to de-string.

| Boundary file | Ownership sentence |
| --- | --- |
| `KernelPackageBoundaryContracts` | Consumer path ↛ Kernel; hosting no *direct* Kernel; Kernel graph = Abstractions only |
| `ContractsPackageBoundaryContracts` | Contracts free of Kernel/SDK; MCP providers → Integrations.Mcp; Tasks independence |
| `HostingPackageBoundaryContracts` | Northbound Ui/MCP vs product/Quickstart silo graphs |
| `CompositionBoundaryContracts` | Pre-rail compositions = client + contracts only (Hold #14) |
| `AssemblyBoundaryContracts` | Runtime assembly reach + Kernel UI name purity + Aspire client ↛ Aspire.Hosting |
| `AiContractBoundaries` | AI surface grammar; `LlmAttribute` not public export |
| `PackablePackageBoundaryContracts` | Packable tree = inventory |
| `PackageBoundarySupport` / `RepositoryLayout` | Shared graph oracles + repo root |

Mid-band scoped verify (Agent 149): Boundary filter **97/97**. No Boundary file >400 lines.

#### Agent 161 fold — Packages / Hosting / Flutter / L1 (remainder 150–172)

| Band | Scope | Folded result @161 |
| --- | --- | --- |
| **150–152** | `Packages/*` | Residual graphs exact; `TimeContracts` Countdown-only PE; `TasksContracts` task/worker PE + runtime Module-only; `ClientApiContracts` Get/Send/Emit; `AspireContracts` consumer vs AppHost split; `PackageInventory` spine |
| **153–156** | Hosting L0 | FlutterHosting HostMode/Selection/UiEdge + HostingProjection; HostingPackageBoundary **4** host sentences (MCP/Ui/silo/Quickstart) re-proofed inside full Tests suite |
| **157–160** | FlutterContracts + siblings | First-five vocabulary; dual golden wire; runtime export = `FlutterModule` only; hosting public = projection API (Desktop/Headless, no Auto); no Built-live claim |
| **161** | Wave close | This exit; scoped re-proofs quoted; soft residuals listed; root **not claimed** |
| **162–172** | L1 / residual stubs | Folded — no second exit body; protect Explicit live + Designed absences |

#### L1 / edge project re-proof map (Agent 161 — **not** root slnx)

| Project | Passed | Role as witness |
| --- | --- | --- |
| `DigitalBrain.Tests` | **165** | Boundary + Packages + Hosting + Flutter L0 ownership pins |
| `DigitalBrain.Tests` filter Packages residual+family+Flutter | **51** | Residual graphs + PE vocabulary band (scoped) |
| `DigitalBrain.Ui.Tests` | **8** (+ Explicit live held off default) | Edge routes/SSE + `UiEdgeVocabulary`; live product northbound Explicit |
| `DigitalBrain.Flutter.Tests` | **9** | Shell/scene journals + `FlutterVocabulary` |
| `DigitalBrain.Integrations.Tests` | **14** | Gmail/SF L1 + `GoogleContracts` Gmail-only |
| `DigitalBrain.Time.Tests` | **19** | Countdown lifecycle/recovery vocabulary |
| `DigitalBrain.Tasks.Tests` | **6** | Task lifecycle via test-only `ScriptedWorker` |
| `DigitalBrain.ModuleTests` | **6** | AI smoke + orchestration L1; supervised throws |
| `DigitalBrain.TestingTests` | **11** | Harness contracts (not product OS) |
| `DigitalBrain.Compositions.Tests` | **8** | Pre-rail sealed compositions; client+contracts surfaces |
| `DigitalBrain.HostTests` | **3** | Residual L2 `TestingAppHostFixture` only — ↛ product catalog |
| `DigitalBrain.Quickstart.Tests` | **1** | Sample greeter journal (not Behavior rail) |

#### Theater / soft residuals after G5 (do **not** invent product API)

| Hold | Location | Status after 161 | Next |
| --- | --- | --- | --- |
| Host assembly name pins | `HostingPackageBoundaryContracts` | **Keep** | Until hosts ship typed surface worth binding |
| Provider SDK / NuGet prefix lists | `PackageBoundarySupport` | **Keep** | Inventory polish only; no re-scatter |
| Kernel UI name-fragment ban | `AssemblyBoundaryContracts` | **Keep** | Defensive purity |
| Session-journal private field **type** pin | `UiEdgeVocabulary` (`GetFields` → `ISessionNeuron`) | **Soft keep** | Ownership proof that SSE uses host-private journal (not `IDigitalBrain` watch / OTel) — pins **field type**, not field name; optional future public shape if rename churn hurts |
| Optional Testing PE pin (13 public types) | Testing library | **Not required** | No surface-creep red this campaign; TestingTests **11/11** + Agent 83 inventory remain oracles — do not invent pin for vanity |
| Family PE Module-marker pins | Time/Tasks/Flutter Packages | **Present on disk** | Protect; do not move into Boundary |
| Explicit live product Ui | `LiveProductUiNorthbound` | **Held Explicit** | Hold #6 — never promote unit green to Built-live |
| HostingPublicApi completeness gap | methods/props only | Soft | Not wrong ownership |

#### What G5 does *not* claim (anti-fake-green)

- Root `dotnet build|test DigitalBrain.slnx` / docs npm green (G7)
- Product AppHost OS Healthy / Built-live Flutter (Hold #6)
- Behavior install rail / calendar Time / supervised product `IWorker` Built (Holds #4/#8/#9)
- That Agent **161** authored concurrent test C# WIP (FlutterVocabulary, TasksContracts, AspireContracts, UiEdgeVocabulary, GoogleContracts, UiHostComposition delete, …) — **foreign peers**; left unstaged; do not reverse
- G4 samples wave complete (still open residual band **129–148**)

#### Scoped verify (Agent 161 — not root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release
→ Passed: 165, Failed: 0

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ResidualPackageGraphContracts|…|FlutterContracts"
→ Passed: 51, Failed: 0

dotnet test tests/DigitalBrain.Ui.Tests -c Release
→ Passed: 8, Failed: 0  (Explicit live held off default run)

dotnet test tests/DigitalBrain.Flutter.Tests|Integrations.Tests|Time.Tests|
  Tasks.Tests|ModuleTests|TestingTests|Compositions.Tests|HostTests|Quickstart.Tests
→ all Failed: 0 (counts in table above)
```

HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`. Foreign concurrent WIP across `tests/**`, hosts, modules, src — **not staged by Agent 161**.

**Verdict:** Wave G5 **COMPLETE**. Tests act as architecture witnesses: package graphs, public vocabulary/edge, Designed absences, Explicit live. Success = assessed + residuals honest — **not** root gate, **not** Built-live, **not** inventing Behavior/`IReminder`.

*End Wave G5 (agents 149–172; Agent 149 Boundary mid + Agent 161 residual close). Scorecard only for Agent 161. Root gate not claimed.*

### Wave G6 — Docs honesty (agents 173–188)

**WAVE G6 COMPLETE with honest residuals** (Agent **173** docs-honesty closer; residual agents **174–188** folded as stubs — no separate product invent).  
Built vs Designed vs Built-live audited on `docs/architecture.md` + `docs/packages.md`.  
**Protect forever (Designed absence):** Behavior proposal/install rail (no `IBehavior` / runner / public behavior test API); calendar Time / `IReminder` / recurrence (Countdown only Built).  
**Still residual (not G6 fail):** Hold #6 product AppHost OS Healthy not Built-live; Hold #7 product journal observation on `IDigitalBrain` Designed; Hold #4 supervised AI `IWorker` Designed; live cloud OAuth L1 residual.  
Root build/test/npm → **G7 only** (not claimed here).

### Wave G7 — Full gates + close (agents 189–200)

_Open._ Hold #16; quote build/test/npm; hard stop 200.

---

## Wave G1 AI family exit (agents 17–24) — **COMPLETE with honest residuals**

**Exit criteria (prompt §7 G1):** *ships neurons/synapses? hides SDK? hosting optional package correct?*

Quoted at finalize (Agent 24, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 24 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status -sb (relevant)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI/LLM/LLM.cs
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/AssemblyInfo.cs
```

**Foreign dirty (Agent 24 did not author product C#):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `modules/.../AI/LLM/LLM.cs` | `LlmAttribute<>` **public → internal** | Concurrent G1 AI peer — Hold #3 close on WIP; **do not reverse** |
| `modules/.../AI/AssemblyInfo.cs` | `InternalsVisibleTo("DigitalBrain.Tests")` | Friend so boundary pin can still typeof `LlmAttribute<>` |
| `docs/packages.md` | MEAI honesty (G0-era foreign) | Keep |
| Kernel `InvokeAsync` delete | G0-era foreign | Keep; G2 re-proof |
| This scorecard | Agent 16 + 24 write scope | Campaign record |

**Note:** a mid-cycle soft of `AiContractBoundaries` was observed then **reverted** on disk — current pin still asserts `typeof(LlmAttribute<>)` via IVT (stronger residual pin intact).

Agents 17–23 folded from on-disk re-proof + Agent 17 return (contracts deep) + concurrent WIP.

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships neurons / synapses | **PASS** | **Contracts** public surface = neuron interfaces only: `ILLM`, `IAgent`, `IGroupChat` (`IAgent`+`IWorker`), `ILlama32` (`DigitalBrain.AI.Ollama`), `IGpt56` (`DigitalBrain.AI.OpenAI`). Wire = MEAI `ChatMessage`/`ChatResponse` (architecture §4.1 deliberate — not provider SDK, not MAF). No synapse fact records in AI contracts (request/reply neuron surface by design). **Runtime** public author bases: `LLM`, `Llama32`, `Gpt56`, `Concurrent`, `GroupChat`, `Participant`/`Participant<T>`, `AIModule` — §4.1 ratified vocabulary, **kept**. `LlmAttribute<>` no longer public (WIP demote). |
| Hides SDK / MAF | **PASS** (residual Holds #4–5) | **Contracts** csproj: `Microsoft.Extensions.AI.Abstractions` only among AI packages — **zero** `Microsoft.Agents.*`, OllamaSharp, OpenAI client packages. Grep contracts source: no MAF/OpenAI/Ollama SDK usings. **Runtime** owns SDKs (`Microsoft.Agents.AI.Workflows`, `Microsoft.Extensions.AI.OpenAI`, `OllamaSharp`) and keeps execution internals **`internal`**: `DirectAgentSession`, `DirectOrchestrationShape`, `OrchestrationDefinition`, `MafParticipantAdapter`, `NeuronChatClient`, `AIClients`, and (WIP) `LlmAttribute<>`. Public runtime does **not** re-export MAF agent types on contracts. `IChatClient` injection confined to concrete `LLM` neurons via keyed DI. |
| Hosting optional package correct | **PASS** | Separate packable `DigitalBrain.Modules.AI.Aspire.Hosting`: public `AIHostingExtensions.WithLlm<TModel>()` only; provider Aspire packages (`Aspire.Hosting.OpenAI`, `CommunityToolkit.Aspire.Hosting.Ollama`) live **only** there. Projection owns one Ollama / one OpenAI resource per brain + secret API-key parameter — matches packages.md / §4.1 (no routing tier, no per-model credentials). Optional: silo can select `AIModule` without the hosting package when env is wired elsewhere. |
| Residual holds (not fake green) | **PASS** | Hold **#3 CLOSED on WIP**; Holds **#4–5** remain — G1 AI does **not** claim supervised Built, does **not** delete §4.1 bases, does **not** claim root gate |

### Package role map (re-proof)

| Package | Public product surface | Must stay out |
| --- | --- | --- |
| `DigitalBrain.Modules.AI.Contracts` | `ILLM`, `IAgent`, `IGroupChat`, `ILlama32`, `IGpt56` + MEAI messages | Provider SDKs, MAF types, `IChatClient` |
| `DigitalBrain.Modules.AI` | §4.1 bases/models + `AIModule` + `Participant*` (`LlmAttribute` internal on WIP) | MAF/OpenAI/Ollama types on public contracts; second agent loop |
| `DigitalBrain.Modules.AI.Aspire.Hosting` | `WithLlm<TModel>` projection | Runtime neuron logic; client-side secrets on non-silo refs |

### What G1 AI does *not* claim (anti-fake-green)

- Supervised `IGroupChat` `Accept`/`Continue`/`Cancel` as Built — runtime **throws** (`SupervisedNotImplemented`); architecture §4.1 + packages.md: **Designed**
- `Sequential` / `Handoff` / `Magentic` bases standing
- Conversation compaction / token budget product
- Root `dotnet build|test` / docs npm green for this campaign (Agent 24 docs-honesty only — **not run / not claimed**)
- That Agent 24 authored the `LlmAttribute` demote — **foreign concurrent peer**; left unstaged by Agent 24

### Holds after AI family grill

| # | Hold | Status after 17–24 | Residual recommendation |
| --- | --- | --- | --- |
| 3 | **`LlmAttribute<>` public** | **CLOSED on WIP tree** | Peer demoted `internal` + IVT; pin still typeof `LlmAttribute<>`. G7/root gate must still prove build/test green on that WIP; do not re-public without product author need |
| 4 | **Supervised `IWorker` on `IGroupChat`** | **Still open (Designed)** | Contracts extend `IWorker`; runtime throws with explicit message. Do not fake Built; thin Orleans-primary path later or keep Designed explicit in docs/tests |
| 5 | **AI.Contracts → MEAI.Abstractions** | **Still open (deliberate)** | Keep packages.md honesty; reject OpenAI/Ollama/MAF creep onto Contracts |

**Closed / protected this family (do not re-open as product trash):** delete of §4.1 public bases `LLM`/`Concurrent`/`GroupChat`/`Llama32`/`Gpt56`; provider SDK on Contracts; MAF Durable Extension / Harness-as-core; routing/failover/cost tier; Kernel AI domain knowledge; re-public of `LlmAttribute` without author need.

### Peer summary (agents 17–23 → 24)

| Agent band | Focus (prompt) | Folded result |
| --- | --- | --- |
| 17 | Contracts deep (`contract-surface`) | **G1-clean:** 5 neuron interfaces only; MEAI deliberate; no provider/MAF; do not strip `IWorker` from `IGroupChat`; scoped Contracts build 0/0 + boundary filter 94 pass (**not** root gate); no product C# edit |
| 18–19 | Contracts / surface remainder | Aligns Agent 17 inventory; Holds #4–5 residual |
| 20–22 | Runtime encapsulate | MAF/session/adapters internal; §4.1 bases public by design; concurrent peer closed #3 (`LlmAttribute` → `internal` + IVT) |
| 23 | Aspire.Hosting | Optional package; `WithLlm` owns provider resources; no Auto-style hosting |
| 24 | Docs-honesty exit | This block; surface foreign AI WIP; residual map + cycle log; root gate unclaimed |

**Verdict:** AI family **ownership aligns** with architecture §4.1 and packages.md for Built direct surface. Success = assessed + residual holds honest — **not** inventing supervised Built or deleting ratified bases. Hold #3 closed on concurrent WIP only until a green boundary claims it.

*End Wave G1 AI family (agents 17–24). Agent 24 wrote scorecard only. Root gate not claimed.*

---

## Wave G1 Tasks family exit (agents 25–32) — **COMPLETE with honest residuals**

**Exit criteria (prompt §7 G1):** *ships neurons/synapses? hides implementation? hosting optional package correct?*  
Tasks-specific answers: **ships neurons?** · **hides impl?** · **no Aspire hosting needed?** · residual: **supervised product `IWorker` Designed**.

Quoted at finalize (Agent 32, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 32 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status -sb (relevant)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI.Aspire.Hosting/AIHostingExtensions.cs
 M modules/DigitalBrain.Modules.AI/LLM/LLM.cs
 M modules/DigitalBrain.Modules.AI/Orchestration/MafParticipantAdapter.cs
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
 M tests/DigitalBrain.ModuleTests/OrchestrationL1.cs
 M tests/DigitalBrain.Tests/Boundary/AiContractBoundaries.cs
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/Orchestration/Participant.cs
```

**Foreign dirty (Agent 32 did not author product C#; none of it is Tasks):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| AI `LLM.cs` / `MafParticipantAdapter.cs` / `Participant.cs` / `AIHostingExtensions.cs` / `AiContractBoundaries.cs` / `OrchestrationL1.cs` | Concurrent G1 AI WIP (incl. `LlmAttribute` demote + IVT friends) | **Do not reverse**; G1 AI exit owns narrative |
| `docs/packages.md` | MEAI honesty (G0-era foreign) | Keep |
| Kernel `InvokeAsync` delete | G0-era foreign | Keep; G2 re-proof |
| This scorecard | Agents 16 + 24 + 32 write scope | Campaign record |
| **Tasks packages** | **Clean — no porcelain under `modules/DigitalBrain.Modules.Tasks*`** | G1 Tasks assessed in place; no product edit required |

Agents 25–31 folded from on-disk re-proof (contracts inventory, runtime encapsulate, package graph, architecture §4.2 / packages.md alignment). No concurrent Tasks product WIP observed.

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships neurons / synapses | **PASS** | **Contracts** public surface = neuron interfaces + task/attempt vocabulary only: `ITask` (`Start`/`Cancel`/`Read` → `TaskSnapshot`), `IWorker` (`Accept`/`Continue`/`Cancel`), commands (`StartTask`, `CancelTask`, `TaskPolicy`), snapshot/state (`TaskSnapshot`, `TaskState`), extension points (`Goal`/`Result`/`Failure` abstract), ids (`AttemptId`, `BlockerId`, `FactReference`), worker wire (`AttemptRequest`, `AttemptCursor`), attempt **synapses** (`AttemptFact` + `AttemptAccepted`/`Progressed`/`Waiting`/`Succeeded`/`Failed`/`Cancelled`/`OutcomeUncertain`), typed blockers (`TaskBlocker` + `InputRequired`/`ApprovalRequired`/`DependencyPending`/`RetryScheduled`/`OutcomeUncertain`). Contracts csproj references **only** `DigitalBrain.Abstractions` (+ source gen analyzer). Namespace `DigitalBrain.Tasks`. No provider/SDK types. |
| Hides implementation | **PASS** | **Runtime** public product surface = **`TasksModule` only** (selection marker for `AddModule<T>`). `TaskNeuron` is `internal sealed` (`[GrainType("task")]`); all lifecycle partials, `TaskData`, and `PendingWorkerDispatch` / `AcceptWorkerDispatch` / `ContinueWorkerDispatch` / `CancelWorkerDispatch` are **internal**. Runtime csproj: `Tasks.Contracts` + `Kernel` only — **zero** AI, MAF, OpenAI, Ollama, MEAI, Integrations, Time project/package refs. Grep of Tasks Contracts+runtime source for `Microsoft.Agents` / `Microsoft.Extensions.AI` / `OpenAI` / `Ollama` / `MAF` / `Aspire` / `ChatMessage` / `IChatClient`: **no matches**. Boundary pin `TasksRemainIndependentFromAiAndProviders` asserts direct compile refs = Kernel + Tasks.Contracts and reachable set excludes AI/Google/Salesforce/Integrations.Mcp. |
| No Aspire hosting needed | **PASS** | packages.md family row: Tasks module hosting package = **no**. On-disk: **no** `DigitalBrain.Modules.Tasks.Aspire.Hosting` directory (`Test-Path` → False). Correct for a pure durable grain module with private Orleans reminders (`tasks.retry` / `tasks.dispatch`) and no external provider resources to project. Optional hosting package would be trash invent. |
| Residual holds (not fake green) | **PASS** | Product supervised `IWorker` remains **Designed** — G1 Tasks does **not** claim a product worker under `modules/`, does **not** claim AI supervised Built, does **not** claim root gate |

### Package role map (re-proof)

| Package | Public product surface | Must stay out |
| --- | --- | --- |
| `DigitalBrain.Modules.Tasks.Contracts` | `ITask`, `IWorker`, attempt/task synapses, blockers, commands, snapshot/state, `Goal`/`Result`/`Failure` extension bases | AI/MAF/provider SDKs, Time, Integrations, concrete domain goals |
| `DigitalBrain.Modules.Tasks` | `TasksModule` only; neuron/dispatch/persistence **internal** | Public `TaskNeuron`; AI/MAF knowledge; Time dependency for retry; Aspire projection |
| *(no)* `…Tasks.Aspire.Hosting` | — | Do not invent |

### What G1 Tasks does *not* claim (anti-fake-green)

- Product supervised `IWorker` under `modules/` that emits attempt facts — **absent**; architecture §4.2 + residual map: **Designed**
- AI `IGroupChat` `Accept`/`Continue`/`Cancel` as Built — runtime still **throws** (`SupervisedNotImplemented`); Hold **#4** still open (owned jointly with G1 AI)
- That L1 green proves product orchestration — L1 is closed via **test-only** `ScriptedWorker` in `tests/DigitalBrain.Tasks.Tests/TasksHarnessModule.cs` (`internal`, harness module), not a product module worker
- Root `dotnet build|test` / docs npm green for this campaign (Agent 32 docs-honesty only — **not run / not claimed**)
- Any Tasks C# authorship this wave — family already ownership-aligned; Agent 32 scorecard only

### Holds after Tasks family grill

| # | Hold | Status after 25–32 | Residual recommendation |
| --- | --- | --- | --- |
| 4 | **Supervised `IWorker` on `IGroupChat`** (product worker path) | **Still open (Designed)** — reaffirmed | Contracts keep `IWorker` vocabulary; AI runtime throws; Tasks runtime dispatches to any `IWorker` grain id. Do not fake Built product worker; thin Orleans-primary supervised path later. L1 harness `ScriptedWorker` stays test-only. Docs (architecture §4.2 / packages.md) already honest — protect that honesty in G5/G6 |
| — | Tasks package purity / no Aspire host | **CLOSED as G1-clean** | Do not add Tasks.Aspire.Hosting without a real external resource; do not leak `TaskNeuron` public; keep AI→Tasks.Contracts one-way (never reverse) |

**Closed / protected this family (do not re-open as product trash):** inventing Tasks Aspire hosting; publicizing `TaskNeuron`/dispatch records; Tasks→AI or Tasks→Time package edges for “convenience”; claiming test-only `ScriptedWorker` as product; faking supervised Built via Tasks docs.

### Peer summary (agents 25–31 → 32)

| Agent band | Focus (prompt) | Folded result |
| --- | --- | --- |
| 25–27 | Contracts deep (`contract-surface`) | **G1-clean:** neurons `ITask`/`IWorker` + full attempt/task synapse vocabulary; Abstractions-only deps; extension `Goal`/`Result`/`Failure` not concrete AI types; do not strip `IWorker` (AI bridge surface) |
| 28–30 | Runtime encapsulate | `TaskNeuron` internal; dispatch/persistence internal; only `TasksModule` public; zero AI/MAF/SDK; private reminders for retry/dispatch (not Time) |
| 31 | Hosting / package graph | No Aspire.Hosting package — **correct absence**; boundary pin independence; packages.md “no” hosting row matches disk |
| 32 | Docs-honesty exit | This block; residual map + cycle log + checklist; foreign AI WIP surfaced (not Tasks); root gate unclaimed |

**Verdict:** Tasks family **ownership aligns** with architecture §4.2 and packages.md for Built durable task surface. Success = assessed + residual supervised product `IWorker` honest as **Designed** — **not** inventing a product worker or an Aspire host package.

*End Wave G1 Tasks family (agents 25–32). Agent 32 wrote scorecard only. Root gate not claimed.*

---

## Wave G1 Time family exit (agents 33–36 + residual 41) — **COMPLETE with honest residuals**

**Exit criteria (prompt §7 G1):** *ships neurons/synapses? hides implementation? hosting optional package correct?*  
Time-specific answers: **ships `ICountdown` only?** · **hides `CountdownNeuron`?** · **no Aspire.Hosting?** · **tests vocabulary-honest after agent 36?** · residual: **protect `IReminder` absence; PE export pin optional**.

Quoted at finalize (Agent 41, docs-honesty / own-audit residual). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

**Numbering note:** campaign prompt table lists Time as agents **33–40** and Google as **41–48**. Orchestrator compressed Time work to **33–36** and assigned this residual docs-honesty closer as **Agent 41**; Google mid-band already ran Agents **37** / **40** in parallel under the original table. This block is the **Time family exit**, not a Google agent.

### Git ground truth @ Agent 41 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status -sb (relevant)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI.* (foreign concurrent G1 AI WIP)
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
 M tests/DigitalBrain.ModuleTests/OrchestrationL1.cs
 M tests/DigitalBrain.Tests/Boundary/AiContractBoundaries.cs
 M tests/DigitalBrain.Tests/Boundary/ContractsPackageBoundaryContracts.cs
 M tests/DigitalBrain.Tests/Packages/TimeContracts.cs
 M tests/DigitalBrain.Time.Tests/CountdownLifecycle.Validation.cs
 M tests/DigitalBrain.Time.Tests/CountdownRecovery.cs
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/Orchestration/Participant.cs
?? tests/DigitalBrain.Tests/Packages/TasksContracts.cs
```

**Foreign dirty (Agent 41 did not author product C#; Time product modules clean):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `tests/.../Packages/TimeContracts.cs` | Strengthens `IReminder` absence; adds **runtime public-export pin** (`TimeModule` only) + compile-graph pin (Kernel + Time.Contracts) | Concurrent G1 Time peer (agents 33–36 band) — **do not reverse**; PE export pin residual **present on WIP** |
| `tests/DigitalBrain.Time.Tests/CountdownRecovery.cs` | DisplayNames + method renames: product `CountdownElapsed` vocabulary (`FailedElapsedCommit*`, `AssertElapsedCommitFails`, …) | Agent 36 vocabulary honesty — **keep** |
| `tests/DigitalBrain.Time.Tests/CountdownLifecycle.Validation.cs` | Deletes `ReceiptsRetainOnlyTheLatestSixtyFourCommands` (pinned private `MaximumReceipts = 64`) | Agent 36 — remove internals-theater; behavior still enforced by runtime const |
| `tests/.../Boundary/ContractsPackageBoundaryContracts.cs` | Tasks independence now forbids Time project reachability + stronger Tasks.Contracts Abstractions-only assert | Concurrent Tasks/boundary peer — **keep**; not Time product surface |
| AI / Kernel / packages.md / TasksContracts.cs untracked | Concurrent G1 AI + Tasks peers | **Do not reverse** |
| **`modules/DigitalBrain.Modules.Time*`** | **Clean — no porcelain under Time product packages** | G1 Time assessed in place; no product edit required for exit |

Agents 33–36 folded from on-disk re-proof (contracts inventory, PE reflect on Release DLLs, runtime encapsulate, package graph, architecture §4 Time / packages.md, Time.Tests DisplayName honesty). No concurrent Time **product** WIP observed under `modules/`.

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships neurons / synapses (`ICountdown` only) | **PASS** | **Contracts** public vocabulary (namespace `DigitalBrain.Time`) = exactly: `ICountdown`, `StartCountdown` / `RescheduleCountdown` / `CancelCountdown` / `RestartCountdown`, `CountdownElapsed` (synapse), `CountdownSnapshot`, `CountdownStatus`, `CountdownResolution`. Methods: `Start` / `Reschedule` / `Cancel` / `Restart` / `Read` → `Task<CountdownSnapshot>`; unsuffixed + `[Alias]`; `[ClientEntryPoint]` on `ICountdown`. Contracts csproj: **Abstractions only** (+ SourceGeneration analyzer, non-compile). deps.json top-level: `DigitalBrain.Abstractions` only. **No** `IReminder`, `ITimer`, `IRecurringSchedule`, `ScheduleReminder`, `ReminderSnapshot`. Pin: `CountdownIsTheOnlyTimeNeuronCapability` asserts inventory + `Assert.Null(…IReminder)` on both assemblies. |
| Hides `CountdownNeuron` / implementation | **PASS** | **Runtime** public product surface = **`TimeModule` only** (`public sealed partial class TimeModule : IModule`). PE reflect (`artifacts/agent35-reflect` on Release DLL): `Public DigitalBrain.Time.TimeModule`; **`NotPublic DigitalBrain.Time.CountdownNeuron`** and **`NotPublic DigitalBrain.Time.CountdownState`**. Source: `internal sealed partial class CountdownNeuron : Neuron, ICountdown, IRemindable` (`[GrainType("countdown")]`); state + recovery partials internal. Private Orleans reminder wake (`time.countdown.` prefix, `IRemindable.ReceiveReminder` explicit private) — **not** product `IReminder` vocabulary (architecture: callers never see `IGrainReminder` / raw reminder names). Runtime csproj: Time.Contracts + Kernel only. Pin: `RuntimePublicSurfaceIsModuleMarkerOnly` → `Assert.Equal([nameof(TimeModule)], exported)`. |
| No Aspire hosting needed | **PASS** | packages.md family row: Time module hosting package = **no**. On-disk: **no** `DigitalBrain.Modules.Time.Aspire.Hosting` directory (`Test-Path` → False). Correct for durable one-shot grain with private Orleans reminders and no external provider resources to project. Inventing a hosting package would be trash. |
| Tests vocabulary-honest after agent 36 | **PASS** (WIP) | `CountdownRecovery` DisplayNames and helpers speak product `CountdownElapsed` / host-restart / commit-failure outcomes — not grain-orphan / occurrence-internal jargon as the public claim. Deleted receipt-cap L1 that asserted private `MaximumReceipts` retention window (internals theater). Lifecycle + recovery still address `ICountdown` / `CountdownSnapshot` / `CountdownElapsed` only via `TestNeuron<ICountdown>`. Fixture selects `TimeModule` only. |
| Residual holds (not fake green) | **PASS** | Hold **#9** remains **Designed** (calendar / `IReminder` / recurrence unbuilt) — G1 Time **protects absence**, does **not** invent product API, does **not** claim root gate |

### Package role map (re-proof)

| Package | Public product surface | Must stay out |
| --- | --- | --- |
| `DigitalBrain.Modules.Time.Contracts` | `ICountdown` + countdown commands / `CountdownElapsed` / snapshot / status / resolution | `IReminder`, recurrence/calendar types, provider SDKs, Kernel, Orleans reminder types on contracts |
| `DigitalBrain.Modules.Time` | `TimeModule` only; `CountdownNeuron` / `CountdownState` **internal**; private reminder wake | Public `CountdownNeuron`; public reminder product API; AI/Tasks/Google/Salesforce/Integrations edges; Aspire projection |
| *(no)* `…Time.Aspire.Hosting` | — | Do not invent |

### Scoped verify (Agent 41 — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~TimeContracts|FullyQualifiedName~TasksRemainIndependent"
→ Passed: 5, Failed: 0
  (4× TimeContracts pins + Tasks independence forbid Time reachability)
```

PE oracle (Release runtime DLL): sole public type `DigitalBrain.Time.TimeModule`; `CountdownNeuron` / `CountdownState` NotPublic.  
PE oracle (Release contracts DLL): public hand-authored types = countdown vocabulary only; no `IReminder`.

Root `dotnet build|test DigitalBrain.slnx` / docs npm **not claimed**.

### What G1 Time does *not* claim (anti-fake-green)

- Calendar `IReminder`, absolute reminders, recurring interval/calendar, DST records, recurrence library — architecture + packages.md: **Designed / unbuilt**
- That private Orleans `IRemindable` on `CountdownNeuron` is a public product reminder API — it is **wake authority**, hidden behind `ICountdown`
- Product authorship of Time C# this residual — modules already ownership-aligned; Agent 41 scorecard only
- That concurrent pin/test WIP is committed — **foreign dirty** until a green boundary stages it; do not reverse
- Root `dotnet build|test` / docs npm green for this campaign

### Holds after Time family grill

| # | Hold | Status after 33–36+41 | Residual recommendation |
| --- | --- | --- | --- |
| 9 | **Calendar `IReminder` / recurrence** | **Still open (Designed)** — absence **re-proven** | Keep contracts inventory + runtime export pins; reject any public `IReminder` / `ITimer` / recurrence product type until architecture freezes shape with red→green proofs. G5/G6 protect docs honesty |
| — | Time package purity / no Aspire host / neuron hide | **CLOSED as G1-clean** | Do not add Time.Aspire.Hosting without a real external resource; do not publicize `CountdownNeuron`/`CountdownState`; keep private reminder names module-owned |
| — | PE export pin (optional residual) | **Present on WIP** (`RuntimePublicSurfaceIsModuleMarkerOnly`) | Optional residual **satisfied on working tree**; leave pin; G7 must still prove green with that WIP staged |

**Closed / protected this family (do not re-open as product trash):** inventing `IReminder` / calendar product API; inventing Time Aspire hosting; publicizing `CountdownNeuron` or durable state types; treating Orleans `IRemindable` as public schedule vocabulary; re-introducing internals-theater proofs of private receipt caps as product claims.

### Peer summary (agents 33–36 → residual 41)

| Agent band | Focus | Folded result |
| --- | --- | --- |
| 33–34 | Contracts deep (`contract-surface`) | **G1-clean:** `ICountdown` + 8 companion types only; Abstractions-only deps; `IReminder` absent by design |
| 35 | Runtime encapsulate + PE | `CountdownNeuron`/`CountdownState` internal; PE: only `TimeModule` public; private `IRemindable` wake |
| 36 | Tests vocabulary honesty | Recovery/lifecycle DisplayNames use product elapsed vocabulary; drop receipt-cap internals theater |
| 41 | Docs-honesty residual exit | This block; Hold #9 protected; PE pin optional residual closed as present-on-WIP; residual map + cycle log; root gate unclaimed |

**Verdict:** Time family **ownership aligns** with architecture (Time / schedule rules) and packages.md for Built Countdown-only surface. Success = assessed + `IReminder` absence protected as **Designed** — **not** inventing calendar Time or an Aspire host package.

*End Wave G1 Time family (agents 33–36 + residual 41). Agent 41 wrote scorecard only. Root gate not claimed.*

---

## Wave G1 Google — Agent 40 mid-band (package graph + `GoogleModule`) — **G1-clean, not family exit**

**Mission:** `own-audit`  
**Write scope:** Google **csproj package graph**; **`GoogleModule`**; residual scorecard notes.  
**Not this agent:** full Google family exit (remaining peers / docs-honesty closer); Contracts deep (Agent 37 peer already **G1-clean**); live OAuth/cloud L1.

Quoted at Agent 40 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 40

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status -sb (relevant)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI.* (foreign concurrent G1 AI WIP)
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
 M tests/DigitalBrain.ModuleTests/OrchestrationL1.cs
 M tests/DigitalBrain.Tests/Boundary/AiContractBoundaries.cs
 M tests/DigitalBrain.Time.Tests/* (foreign concurrent Time WIP)
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/Orchestration/Participant.cs
```

**Google packages porcelain:** **clean** — no dirty paths under `modules/DigitalBrain.Modules.Google*`. Foreign dirty is AI/Time/Kernel/packages.md/scorecard; **do not reverse**.

### Package graph (csproj + deps.json re-proof)

| Package | Direct compile ProjectReference | Direct PackageReference | Compile-reachable projects | packages.md match |
| --- | --- | --- | --- | --- |
| `DigitalBrain.Modules.Google.Contracts` | `DigitalBrain.Abstractions` (+ SourceGeneration analyzer, `PrivateAssets=all`, non-compile) | **none** | Abstractions | **yes** — “Abstractions” |
| `DigitalBrain.Modules.Google` | Google.Contracts, Integrations.Mcp, Kernel (+ SourceGeneration analyzer) | **none** | Abstractions, Google.Contracts, Integrations.Mcp, Kernel, Security | **yes** — “Google.Contracts, Integrations.Mcp, Kernel” |
| `DigitalBrain.Modules.Google.Aspire.Hosting` | Google runtime, Integrations.Mcp.Aspire.Hosting | **none** | + Aspire.Hosting, Mcp.Aspire.Hosting, full Google runtime graph | **yes** — “Google, Integrations.Mcp.Aspire.Hosting” |

**deps.json direct deps** (Release): Contracts → Abstractions only; Runtime → Integrations.Mcp + Kernel + Google.Contracts; Aspire.Hosting → Integrations.Mcp.Aspire.Hosting + Google. **Zero** direct NuGet PackageReference on any of the three Google csproj files.

**Ownership grill answers (graph):**

| Question | Answer |
| --- | --- |
| Ships neurons/synapses at contracts layer? | **Yes** — `IGmail` + `GmailMessage` only (Agent 37); no tool/OAuth/MCP types |
| Hides SDK? | **Yes** — MCP SDK (`ModelContextProtocol.Core`) owned by Integrations.Mcp; runtime **ProjectReference** only; boundary pin `McpProvidersDependOnSharedMechanics` forbids direct `ModelContextProtocol.Core` / DataProtection / Http on provider runtimes. Admission (`Gmail.Admit`) uses `McpClientTool` **internal** only |
| Hosting optional package correct? | **Yes** — separate packable Aspire.Hosting; public `GoogleHostingExtensions.WithGmail` only; OAuth parameter projection via internal `McpProviderHosting` (IVT friend). Product silo host refs **Google runtime** (`GoogleModule`) not hosting package; AppHost refs hosting for `WithGmail`. Selecting `GoogleModule` without hosting package remains valid if config is wired elsewhere |
| Google → AI edge? | **None** — architecture §4.3: compose at application layer; csproj has no AI project/package refs |
| Salesforce twin? | **Identical graph shape** (Contracts→Abstractions; Runtime→Contracts+Mcp+Kernel; Hosting→Runtime+Mcp.Aspire.Hosting) |

### `GoogleModule` (runtime selection marker)

```csharp
public sealed partial class GoogleModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
    }
}
```

| Check | Result |
| --- | --- |
| Role | `AddModule<GoogleModule>` selection marker + generated `ICompiledModule` capsule (`Id`, `Activate` → `ConfigureRuntime` + broadcast handlers) |
| Public product surface on runtime | **`GoogleModule` only** — `Gmail` is `internal sealed partial class Gmail : Neuron, IGmail` |
| ConfigureRuntime body | Shared southbound only: `McpRuntimeHosting.Configure` (HttpClient named factory, durable payload protection, `TryAddSingleton` session factory + `McpRuntime`). **No** Gmail endpoint/scope/tool policy here — those stay on internal `Gmail` neuron (`McpServerDefinition`, admission, mapping) |
| Dual Configure with Salesforce | Same call from `SalesforceModule`; singletons use `TryAdd*`. Soft residual on Integrations.Mcp: `AddHttpClient` is not `TryAdd` (duplicate named-client registration if both modules activate) — **G2 Integrations (81–88)**, not a Google package-graph fold |
| Belongs on runtime? | **Yes** — not Contracts (would pull Kernel/Mcp into vocabulary consumers); not hosting (silo activation not AppHost projection) |

### Runtime / hosting surface (adjacent to graph, for residual honesty)

| Surface | Visibility | Note |
| --- | --- | --- |
| `Gmail` neuron | `internal` | Owns endpoint `https://gmailmcp.googleapis.com/mcp/v1`, readonly scope, `get_message` admit, `GmailMessage` map |
| `get_message` / tool names | private const + admit logic | Never public vocabulary (architecture §4.3) |
| `GoogleHostingExtensions.WithGmail` | public | Module projection only; no Auto |
| `ICalendar` | **absent** | Designed residual — do not invent |

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Google/DigitalBrain.Modules.Google.csproj -c Release
→ 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Google.Aspire.Hosting/...csproj -c Release
→ 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~ResidualPackageGraphContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~HostingProjectionContracts"
→ Passed: 70, Failed: 0
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm **not claimed**.

### Residuals for remaining Google family / later waves

| Residual | Status | Owner |
| --- | --- | --- |
| Live OAuth / hosted MCP outside default scripted L1 | open (honest Built claim = scripted edge) | G1 Google runtime peers / G3 / G7 live |
| Exact tool admission stays module-owned | **protected** — stay out of Contracts | runtime `Gmail.Admit` |
| `ICalendar` / capability-tool seam | Designed — absent | do not invent |
| AppHost hosting package pulls Kernel via runtime ref | intentional (needs `GoogleModule` type); same Salesforce | soft honesty only — not a delete |
| `McpRuntimeHosting.AddHttpClient` double-call | soft Integrations.Mcp | **G2 81–88** |
| Full G1 Google family exit block | **COMPLETE — Agent 45** | see family exit block below |

### Verdict (mid-band only)

Google **package graph and `GoogleModule` ownership align** with architecture §4.3 and packages.md. Success = assessed + residuals listed — **no product C# edit**. Agent 40 wrote scorecard notes only; root gate unclaimed; family exit closed by **Agent 45**.

*Agent 40 mid-band complete. Agent 37 Contracts peer: G1-clean. Family exit → Agent 45 block.*

---

## Wave G1 Google — Agent 43 (`modules/DigitalBrain.Modules.Google/**` residual dual path) — **G1-clean, not family exit**

**Mission:** `own-audit`  
**Write scope:** `modules/DigitalBrain.Modules.Google/**` residual dual path; **no invent `ICalendar`**. Confirm Gmail only Built.  
**Not this agent:** Contracts (Agent 37); package graph/`GoogleModule` mid-band (Agent 40); hosting package deep; full family exit; live OAuth/cloud L1.

Quoted at Agent 43 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 43

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status --porcelain -- modules/DigitalBrain.Modules.Google
(empty — Google runtime tree clean)

git status --porcelain -- docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
```

**Google packages porcelain:** **clean**. Foreign dirty elsewhere is concurrent campaign WIP — **do not reverse**.

### Disk inventory (write scope — source only)

| Path | Lines (approx) | Role |
| --- | --- | --- |
| `GoogleModule.cs` | 11 | Public selection marker; `ConfigureRuntime` → `McpRuntimeHosting.Configure` only |
| `Gmail/Gmail.cs` | 66 | `internal` neuron: endpoint, scopes, durable tokens, `ReadMessage` via `McpRuntime.RunAsync` |
| `Gmail/Gmail.Admit.cs` | 74 | `internal` partial: positive `get_message` admission (schema + four annotations) |

**No** `Calendar/` folder, `ICalendar` type, REST client, `Google.Apis`, second transport, or public Gmail type under this tree.

### Dual-path grill (what counts as dual)

| Candidate dual | Present? | Verdict |
| --- | --- | --- |
| REST / Gmail HTTP API beside MCP | **No** | Single southbound: hosted MCP only |
| `Google.Apis.*` / GmailService client | **No** | Grep zero under `modules/**/Google*` |
| Second public capability (`ICalendar`, Drive, …) | **No** | Gmail only Built; `ICalendar` Designed vocabulary only in docs |
| Public + internal competing product doors | **No** | Runtime public product surface = `GoogleModule` only; `Gmail` is `internal` |
| Module-owned transport helpers (historical) | **Deleted already** | git history: `GmailMcpBoundary`, `GmailMcpTransport`, `DurableMcpTokenCache`, `GoogleMcpAuthorization`, `McpToolSnapshot` removed when MCP mechanics moved to `Integrations.Mcp` |
| `Gmail.cs` + `Gmail.Admit.cs` partial split | **Not dual** | One neuron, operation vs admission policy files |
| Runtime `McpServerDefinition` vs hosting `McpProviderHostingDefinition` | **Layer split, not dual product path** | Same keys (`google.gmail`, `DigitalBrain:Google:Gmail`); AppHost projects OAuth params; silo owns endpoint/scope/admission — intentional (Agent 40 residual honesty) |

### Call path (codegraph)

1. `IGmail.ReadMessage` (Contracts) → grain `Gmail.ReadMessage`
2. `McpRuntime.RunAsync(Server, tokenState, …)` — shared southbound only
3. `ListToolsAsync` → `AdmitGetMessage` (exact name + schemas + annotations) → `CallAsync` → `RequireStructuredContent` → `GmailMessage`

No alternate branch, no best-effort filter over raw tool dictionaries as public vocabulary, no fingerprint re-path for Gmail (architecture: immediate same-session call; fingerprint is Salesforce durable-later policy).

### Built claim honesty

| Claim | Status | Evidence |
| --- | --- | --- |
| Google family Built | **Gmail only** | packages.md + architecture §4.3; runtime implements `IGmail` only |
| `IGmail.ReadMessage` | Built | L1 scripted MCP admit + annotation refusal (`DigitalBrain.Integrations.Tests`) |
| Live OAuth / hosted MCP cloud | residual | not default L1 — do not overclaim |
| `DigitalBrain.Google.ICalendar` | **Designed — absent** | architecture §4.3 settled-not-standing; **do not invent** |
| Provider-neutral capability-tool seam | Designed — absent | not in this package; do not invent |

### Ownership grill (13 — condensed)

1. **What does it do?** Module selection + one internal Gmail neuron over official hosted MCP.  
2. **Architecture align?** Yes — §4.3 semantic capability, private MCP boundary, admission positive-check.  
3. **Belong here?** Yes — provider policy (endpoint/scope/tools/map) on runtime; transport in Integrations.Mcp.  
4. **Public surface?** `GoogleModule` only in this package; contracts own `IGmail`/`GmailMessage`.  
5. **Hide SDK?** Yes — MCP client types only inside internal admission/runtime callback.  
6. **Dual path?** **None residual.** Historical dual helpers already deleted.  
7. **Wrong layer?** No — admit logic stays out of Contracts (Agent 37).  
8. **Hosting?** Optional package (Agent 40); not this write scope.  
9. **Depend AI?** No.  
10. **ICalendar?** Correctly **absent** — protect.  
11. **Trash delete?** Nothing left in scope.  
12. **New public API?** None.  
13. **Family exit?** **Not this agent.**

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Google/DigitalBrain.Modules.Google.csproj -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm **not claimed**.

### Residuals (honest, not fake green)

| Residual | Status | Owner |
| --- | --- | --- |
| Live OAuth / hosted MCP outside scripted L1 | open | G1 remaining peers / G3 / G7 live |
| Exact tool admission stays module-owned | **protected** | runtime `Gmail.Admit` |
| `ICalendar` / capability-tool seam | Designed — **absent** | do not invent |
| Soft string couple hosting key ↔ runtime `McpServerDefinition` | intentional layer split | soft honesty; not a delete |
| Full G1 Google family exit | **COMPLETE — Agent 45** | see family exit block below |

### Verdict (mid-band only)

**Gmail only is Built.** Runtime tree has **zero residual dual paths** and **does not invent `ICalendar`**. Success = assessed + residuals listed — **no product C# edit**. Agent 43 scorecard notes only; root gate unclaimed; family exit closed by **Agent 45**.

*Agent 43 mid-band complete. Peers: 37 Contracts + 40 graph/`GoogleModule` G1-clean. Family exit → Agent 45 block.*

---

## Wave G1 Google family exit (agents 37–40, 43–45) — **COMPLETE with honest residuals**

**Exit criteria (prompt §7 G1):** *ships neurons/synapses? hides SDK? hosting optional package correct?*  
Google-specific answers: **ships `IGmail`?** · **hides MCP/provider SDK?** · **`WithGmail` hosting optional OK?** · residual: **live cloud L1 + `ICalendar` Designed**.

**Numbering honesty:** Prompt §7 listed Google as agents **41–48**. Continuous campaign already spent **37 Contracts + 40 package graph + 43 runtime dual-path** mid-band and assigned **44** hosting re-proof with **45** as docs-honesty closer (Time residual also used **41**). This exit folds **37–40, 43–45**. Do not invent agents 41–42 product Google work that has no scorecard return (41 = Time residual).

Quoted at finalize (Agent 45, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 45 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- modules/DigitalBrain.Modules.Google modules/DigitalBrain.Modules.Google.Contracts modules/DigitalBrain.Modules.Google.Aspire.Hosting
(empty — Google family trees clean)

git status -sb (relevant foreign)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI.* (foreign concurrent G1 AI WIP)
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
 M tests/DigitalBrain.ModuleTests/OrchestrationL1.cs
 M tests/DigitalBrain.Tests/Boundary/* (foreign concurrent)
 M tests/DigitalBrain.Time.Tests/* (foreign concurrent Time WIP)
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/Orchestration/Participant.cs
?? tests/DigitalBrain.Tests/Packages/TasksContracts.cs
```

**Foreign dirty (Agent 45 did not author product C#; none of it is Google):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| AI `LLM.cs` / `MafParticipantAdapter.cs` / `Participant.cs` / `AIHostingExtensions.cs` / `AiContractBoundaries.cs` / `OrchestrationL1.cs` | Concurrent G1 AI WIP | **Do not reverse**; G1 AI exit owns narrative |
| Time.Tests validation/recovery | Concurrent G1 Time WIP | **Do not reverse**; Time family owns narrative |
| Kernel `InvokeAsync` delete / packages.md MEAI | G0-era foreign | Keep |
| Boundary/Packages test WIP | Concurrent test-truth / G1 peers | Leave; not Google package graph |
| Salesforce mid-band (Agent 49+) | Concurrent G1 Salesforce WIP | Foreign; leave for SF family exit |
| This scorecard | Agents 16 + 24 + 32 + 40 + 41 + 43 + 45 write scope | Campaign record |
| **Google packages** | **Clean — no porcelain under `modules/DigitalBrain.Modules.Google*`** | G1 Google assessed in place; no product edit required |

Agents 37–40, 43–44 folded from on-disk re-proof + Agent 37/40/43 returns + Agent 45 source/deps/test oracles. No concurrent Google product WIP observed.

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships `IGmail` (neurons / result vocabulary) | **PASS** | **Contracts** public surface = `IGmail` (`ReadMessage` → `GmailMessage`) + `GmailMessage` record (`Id`/`Subject`/`Sender`/`PlaintextBody`) only. Namespace `DigitalBrain.Google`. Contracts csproj: **only** `DigitalBrain.Abstractions` (+ SourceGeneration analyzer, non-compile). **Zero** PackageReference. No MCP/OAuth/tool/endpoint types on contracts. No synapse fact records (request/reply neuron surface by design — architecture §4.3: one typed message read). **`ICalendar` correctly absent** (Designed residual — do not invent). |
| Hides SDK | **PASS** | **Runtime** public product surface = **`GoogleModule` only** (`IModule` selection marker). `Gmail` is `internal sealed partial class Gmail : Neuron, IGmail` with private endpoint/scope/tool constants and internal admit partial (`Gmail.Admit`). `using ModelContextProtocol.Client` appears **only** in internal admit — never on contracts or public API. Runtime csproj: Google.Contracts + Integrations.Mcp + Kernel only — **zero** direct NuGet PackageReference. deps.json direct deps: Contracts→Abstractions; Runtime→Integrations.Mcp + Kernel + Google.Contracts. `ModelContextProtocol.Core` reaches runtime **only** transitively through Integrations.Mcp (shared southbound). Boundary pin `McpProvidersDependOnSharedMechanics` forbids direct `ModelContextProtocol.Core` / DataProtection / Http on provider runtimes. Integrations.Mcp `InternalsVisibleTo("DigitalBrain.Modules.Google")` is friendship, not vocabulary export (Hold #10 soft). Single southbound path (Agent 43): `ReadMessage→McpRuntime→AdmitGetMessage` — **no** REST/`Google.Apis` dual. No AI/MAF/OpenAI/Ollama refs in family source. |
| Hosting optional package correct (`WithGmail`) | **PASS** | Separate packable `DigitalBrain.Modules.Google.Aspire.Hosting`: public surface = `GoogleHostingExtensions.WithGmail(this DigitalBrainModuleBuilder<GoogleModule>)` only. Projection registers OAuth parameters via **internal** `McpProviderHosting` (Integrations.Mcp.Aspire.Hosting IVT friend) — no Auto mode. Product **silo** host (`DigitalBrain.Host`) ProjectReferences **Google runtime** only (`GoogleModule`) — not the hosting package. Product **AppHost** ProjectReferences hosting and calls `brain.AddModule<GoogleModule>(google => google.WithGmail())`. Selecting `GoogleModule` without the hosting package remains valid when config is wired elsewhere (L1 Integrations fixture does exactly that: `brain.AddModule<GoogleModule>()` + scripted MCP edge). packages.md row matches: hosting depends on Google + Integrations.Mcp.Aspire.Hosting. |
| Residual holds (not fake green) | **PASS** | Live OAuth / hosted cloud MCP outside scripted L1 remains open; Built claim = package graph + AppHost selection + Integrations.Tests admit/refuse on **scripted** edge. `ICalendar` + capability-tool seam stay Designed-absent. G1 Google does **not** claim live cloud green, does **not** invent calendar vocabulary, does **not** claim root gate |

### Package role map (re-proof)

| Package | Public product surface | Must stay out |
| --- | --- | --- |
| `DigitalBrain.Modules.Google.Contracts` | `IGmail`, `GmailMessage` | MCP/OAuth/tool names, endpoints, scopes, Integrations, Kernel |
| `DigitalBrain.Modules.Google` | `GoogleModule` only; `Gmail` + admit **internal** | Public neuron class; direct MCP SDK PackageReference; REST dual; AI/provider composition |
| `DigitalBrain.Modules.Google.Aspire.Hosting` | `WithGmail` projection | Runtime admit logic; client-side secrets on non-silo refs; Auto hosting |

### Semantic proof (honest tier)

| Tier | Status | Evidence |
| --- | --- | --- |
| L0 package / hosting graph | **green (scoped)** | Agent 45: three Google packages build Release **0/0**; boundary filter **70 pass** (`ContractsPackageBoundaryContracts` + `ResidualPackageGraphContracts` + `HostingPackageBoundaryContracts` + `HostingProjectionContracts`) — **not** root gate |
| L1 scripted MCP | **Built claim** | `DigitalBrain.Integrations.Tests` `GmailReadMessage`: admit `get_message` → `GmailMessage`; refuse incompatible annotations. Fixture selects `GoogleModule` without hosting package. Agent 45 **did not re-run** Integrations.Tests (docs-honesty; boundary filter only). |
| Live OAuth / hosted MCP | **residual** | Not default L1; production interactive auth at edge; LocalLoopbackDevelopment private — architecture §4.3 |

### What G1 Google does *not* claim (anti-fake-green)

- Live Google cloud OAuth / hosted Gmail MCP as default L1 green
- `ICalendar` or capability-tool seam as Built
- That `get_message` is public domain vocabulary — tool name is **private** admit policy only
- Root `dotnet build|test DigitalBrain.slnx` / docs npm green (Agent 45 docs-honesty — **not run / not claimed**)
- Any Google C# authorship this wave — family already ownership-aligned; Agent 45 scorecard only
- Prompt agents 41–42 product Google returns — **no evidence** (41 = Time residual; continuous numbering 37–40, 43–45)

### Holds after Google family grill

| # | Hold | Status after 37–45 | Residual recommendation |
| --- | --- | --- | --- |
| — | Live OAuth / hosted MCP | **Still open (residual, not ownership fail)** | Keep packages.md Built sentence tied to scripted edge + package graph; never promote unit/scripted green to live cloud claim; G3/G7 when product topology proves Healthy |
| — | `ICalendar` / capability-tool seam | **Designed absence protected** | Do not invent public calendar API or model-facing tool escape hatch until concrete product story |
| 10 | Integrations.Mcp IVT friend names | **Soft reaffirmed** | IVT `DigitalBrain.Modules.Google` is friendship for `McpRuntime`/hosting internals — not Gmail vocab in Integrations. Soft G2 pin optional |

**Closed / protected this family (do not re-open as product trash):** publicizing `Gmail` neuron or `get_message` as contracts vocabulary; direct `ModelContextProtocol.*` PackageReference on Google packages; Google→AI package edge; REST/`Google.Apis` dual transport; Auto hosting; inventing `ICalendar` mid-campaign; collapsing hosting into runtime so AppHost secrets leak to silo-only catalog.

### Peer summary (agents 37–40, 43–44 → 45)

| Agent band | Focus | Folded result |
| --- | --- | --- |
| 37 | Contracts deep (`contract-surface`) | **G1-clean:** `IGmail` + `GmailMessage` only; Abstractions-only; no MCP/OAuth/tool names; `ICalendar` absent |
| 38–39 | Contracts / surface remainder | Aligns Agent 37 inventory; no extra public types |
| 40 | Package graph + `GoogleModule` (`own-audit`) | **G1-clean:** three csproj = packages.md; zero direct PackageReference; neuron internal; hosting optional `WithGmail`; mid-band block above |
| 43 | Runtime residual dual path (`own-audit`) | **G1-clean:** Gmail only Built; single MCP path; no REST/Apis dual; historical dual helpers already deleted; `ICalendar` absent |
| 44 | Aspire.Hosting / `WithGmail` optional | **G1-clean:** public `WithGmail` only; OAuth via shared `McpProviderHosting`; silo vs AppHost ref split correct; fixture may omit hosting |
| 45 | Docs-honesty exit | This block; residual map + cycle log + checklist; foreign AI/Time/SF WIP surfaced; root gate unclaimed |

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Google.Contracts -c Release
→ 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Google -c Release
→ 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Google.Aspire.Hosting -c Release
→ 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~ResidualPackageGraphContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~HostingProjectionContracts"
→ Passed: 70, Failed: 0
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm **not claimed**.

**Verdict:** Google family **ownership aligns** with architecture §4.3 and packages.md for Built scripted-MCP Gmail surface. Success = assessed + residuals honest — **not** inventing live cloud Built or calendar vocabulary.

*End Wave G1 Google family (agents 37–40, 43–45). Agent 45 wrote scorecard only. Root gate not claimed.*

---

## Wave G1 Salesforce — Agent 49 mid-band (package graph + `SalesforceModule`) — **G1-clean, not family exit**

**Mission:** `own-audit`  
**Write scope:** Salesforce **csproj package graph**; **`SalesforceModule`**; residual scorecard notes.  
**Not this agent:** full Salesforce family exit (remaining peers / docs-honesty closer); Contracts deep surface grill (Agent 46 concurrent return: **G1-clean**); live OAuth/cloud L1; mutation-path encapsulate beyond graph.

Quoted at Agent 49 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 49

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status -sb (relevant)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI.* (foreign concurrent G1 AI WIP)
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
 M tests/DigitalBrain.ModuleTests/OrchestrationL1.cs
 M tests/DigitalBrain.Tests/Boundary/AiContractBoundaries.cs
 M tests/DigitalBrain.Tests/Boundary/ContractsPackageBoundaryContracts.cs
 M tests/DigitalBrain.Time.Tests/* (foreign concurrent Time WIP)
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/Orchestration/Participant.cs
?? tests/DigitalBrain.Tests/Packages/TasksContracts.cs
```

**Salesforce packages porcelain:** **clean** — no dirty paths under `modules/DigitalBrain.Modules.Salesforce*`. Foreign dirty is AI/Time/Kernel/packages.md/tests/scorecard; **do not reverse**.

### Package graph (csproj + deps.json re-proof)

| Package | Direct compile ProjectReference | Direct PackageReference | Compile-reachable projects | packages.md match |
| --- | --- | --- | --- | --- |
| `DigitalBrain.Modules.Salesforce.Contracts` | `DigitalBrain.Abstractions` (+ SourceGeneration analyzer, `PrivateAssets=all`, non-compile) | **none** | Abstractions | **yes** — “Abstractions” |
| `DigitalBrain.Modules.Salesforce` | Salesforce.Contracts, Integrations.Mcp, Kernel (+ SourceGeneration analyzer) | **none** | Abstractions, Salesforce.Contracts, Integrations.Mcp, Kernel, Security | **yes** — “Salesforce.Contracts, Integrations.Mcp, Kernel” |
| `DigitalBrain.Modules.Salesforce.Aspire.Hosting` | Salesforce runtime, Integrations.Mcp.Aspire.Hosting | **none** | + Aspire.Hosting, Mcp.Aspire.Hosting, full Salesforce runtime graph | **yes** — “Salesforce, Integrations.Mcp.Aspire.Hosting” |

**deps.json direct deps** (Release): Contracts → Abstractions only; Runtime → Integrations.Mcp + Kernel + Salesforce.Contracts; Aspire.Hosting → Integrations.Mcp.Aspire.Hosting + Salesforce. **Zero** direct NuGet PackageReference on any of the three Salesforce csproj files.

**Ownership grill answers (graph):**

| Question | Answer |
| --- | --- |
| Ships neurons/synapses at contracts layer? | **Yes** — `ISalesforce` + mutation receipt/state + `SalesforceMutationApproval` (Synapse); no tool/OAuth/MCP types in Contracts (Agent 46 surface audit) |
| Hides SDK? | **Yes** — MCP SDK owned by Integrations.Mcp; runtime **ProjectReference** only; boundary pin `McpProvidersDependOnSharedMechanics` forbids direct `ModelContextProtocol.Core` / DataProtection / Http on provider runtimes. Admission/invoke uses `McpClientTool` **internal** only |
| Hosting optional package correct? | **Yes** — separate packable Aspire.Hosting; public `SalesforceHostingExtensions.WithSalesforce` only; OAuth parameter projection via internal `McpProviderHosting`. Product silo host refs **Salesforce runtime** (`SalesforceModule`) not hosting package; AppHost refs hosting for `WithSalesforce`. Selecting `SalesforceModule` without hosting package remains valid if config is wired elsewhere |
| Salesforce → AI / Tasks edge? | **None** — architecture §4.4: no AI dependency; no Tasks project/package refs (reconciliation returns receipt only; parking Task Designed). Multi-module compose at sample layer (`AccountEnrichment`) |
| Google twin? | **Identical graph shape** (Contracts→Abstractions; Runtime→Contracts+Mcp+Kernel; Hosting→Runtime+Mcp.Aspire.Hosting) — Agent 40 already noted twin |

### `SalesforceModule` (runtime selection marker)

```csharp
public sealed partial class SalesforceModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
    }
}
```

| Check | Result |
| --- | --- |
| Role | `AddModule<SalesforceModule>` selection marker + generated `ICompiledModule` capsule (`Id`, `Activate` → `ConfigureRuntime` + broadcast handlers) |
| Public product surface on runtime | **`SalesforceModule` only** — PE metadata export of Release DLL is sole public type; `Salesforce` is `internal sealed partial class Salesforce : Neuron, ISalesforce` |
| ConfigureRuntime body | Shared southbound only: `McpRuntimeHosting.Configure` (HttpClient named factory, durable payload protection, `TryAddSingleton` session factory + `McpRuntime`). **No** Salesforce endpoint/scope/tool policy here — those stay on internal `Salesforce` neuron (`McpServerDefinition`, admit, map, mutation ledger) |
| Dual Configure with Google | Same call from `GoogleModule`; singletons use `TryAdd*`. Soft residual on Integrations.Mcp: `AddHttpClient` is not `TryAdd` (duplicate named-client registration if both modules activate) — **G2 Integrations (81–88)**, not a Salesforce package-graph fold |
| Belongs on runtime? | **Yes** — not Contracts (would pull Kernel/Mcp into vocabulary consumers); not hosting (silo activation not AppHost projection) |

### Runtime / hosting surface (adjacent to graph, for residual honesty)

| Surface | Visibility | Note |
| --- | --- | --- |
| `Salesforce` neuron | `internal` | Owns endpoint `https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations`, scopes, `updateSobjectRecord` / `soqlQuery` admit, durable mutation ledger |
| Tool names / Invoking fence | private const + internal `MutationStatus` | Never public vocabulary (architecture §4.3/§4.4); public receipt maps Invoking back to `AwaitingApproval` |
| `SalesforceHostingExtensions.WithSalesforce` | public | Module projection only; no Auto |
| Auto-approve classification / Task parking on `OutcomeUncertain` | **absent (Designed)** | Do not invent in this mid-band |

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Salesforce/DigitalBrain.Modules.Salesforce.csproj -c Release
→ 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/...csproj -c Release
→ 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~ResidualPackageGraphContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~HostingProjectionContracts"
→ Passed: 70, Failed: 0
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm **not claimed**.

### Residuals for remaining Salesforce family / later waves

| Residual | Status | Owner |
| --- | --- | --- |
| Live OAuth / hosted MCP outside default scripted L1 | open (honest Built claim = scripted edge) | G1 Salesforce runtime peers / G3 / G7 live |
| Exact tool admission + schema fingerprints stay module-owned | **protected** — stay out of Contracts | runtime `Invoke` admit |
| Operation auto-approve classification | Designed — absent | do not invent |
| Park Task on `OutcomeUncertain` | Designed — no producer under modules/src | sample/caller ownership; not this package graph |
| AppHost hosting package pulls Kernel via runtime ref | intentional (needs `SalesforceModule` type); same Google | soft honesty only — not a delete |
| `McpRuntimeHosting.AddHttpClient` double-call | soft Integrations.Mcp | **G2 81–88** |
| Full G1 Salesforce family exit block | **not this agent** | remaining Salesforce peers + family docs-honesty closer |

### Verdict

Salesforce **package graph and `SalesforceModule` ownership align** with architecture §4.3–§4.4 and packages.md. Success = assessed + residuals listed — **no product C# edit**. Agent 49 wrote scorecard notes only; root gate unclaimed; **not** a full Salesforce family exit.

*Agent 49 mid-band complete. Peer: Agent 46 Contracts G1-clean. Family exit still open.*

---

## Wave G1 Salesforce — Agent 51 (residual dual path) — **G1-clean, not family exit**

**Mission:** `own-audit`  
**Write scope:** Any Salesforce **dual product path residual**; residual scorecard SF mid notes.  
**Not this agent:** full Salesforce family exit; Contracts deep (Agent 46); package graph/`SalesforceModule` mid-band (Agent 49); live OAuth/cloud L1; product C# fold unless dual found.

Quoted at Agent 51 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 51

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status --porcelain -- modules/DigitalBrain.Modules.Salesforce*
(empty — Salesforce product tree clean)

git status --porcelain (foreign concurrent WIP, not authored by Agent 51)
 M docs/packages.md
 M modules/DigitalBrain.Modules.AI.*
 M src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs
 M tests/DigitalBrain.Integrations.Tests/GmailReadMessage.cs
 M tests/DigitalBrain.Integrations.Tests/SalesforceMutation.cs   # peer G1 SF tests — do not reverse
 M tests/DigitalBrain.ModuleTests/OrchestrationL1.cs
 M tests/DigitalBrain.Tests/Boundary/*
 M tests/DigitalBrain.Time.Tests/*
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
?? modules/DigitalBrain.Modules.AI/Orchestration/Participant.cs
?? tests/DigitalBrain.Integrations.Tests/GoogleContracts.cs
?? tests/DigitalBrain.Tests/Packages/TasksContracts.cs
```

**Salesforce packages porcelain:** **clean** — no dirty paths under `modules/DigitalBrain.Modules.Salesforce*`. Foreign dirty is concurrent campaign WIP (incl. peer SF test facts under Integrations.Tests); **do not reverse**.

### Disk inventory (product SF family — source only)

| Path | Lines (physical) | Role |
| --- | --- | --- |
| `Salesforce.Contracts/ISalesforce.cs` | 20 | Public neuron: Propose + Approve only |
| `Salesforce.Contracts/SalesforceAccountDescriptionMutation.cs` | 21 | Public receipt + `SalesforceMutationState` (3 public states) |
| `Salesforce.Contracts/SalesforceMutationApproval.cs` | 12 | Public approval synapse |
| `Salesforce/SalesforceModule.cs` | 13 | Public selection marker; `ConfigureRuntime` → `McpRuntimeHosting.Configure` only |
| `Salesforce/Salesforce.cs` | 38 | `internal` neuron: endpoint, scopes, durable keys, tool name consts |
| `Salesforce/Propose/Propose.cs` | 53 | Propose = durable record only; **zero MCP** |
| `Salesforce/Approve/Approve.cs` | 150 | Evidence gate → admit tools → Invoking fence → update → reconcile |
| `Salesforce/Invoke/Invoke.cs` | 298 | MCP update + SOQL reconcile + schema admission (≤400 line gate) |
| `Salesforce/State/State.cs` | 122 | Durable mutation ledger + receipt map + fingerprint |
| `Salesforce.Aspire.Hosting/SalesforceHostingExtensions.cs` | 25 | Public `WithSalesforce` only |

**No** REST/SOAP client, Force.com SDK, second public write neuron, host hand-wire OAuth, or northbound-MCP southbound dual under product SF packages.

### Dual-path grill (what counts as dual)

| Candidate dual | Present? | Verdict |
| --- | --- | --- |
| REST / Force.com / SOAP beside MCP | **No** | Single southbound: hosted MCP `sobject-mutations` only |
| Second public write capability | **No** | One neuron `ISalesforce`; one mutating tool name private (`updateSobjectRecord`) |
| Public + internal competing product doors | **No** | Runtime public product surface = **`SalesforceModule` only**; `Salesforce` neuron is `internal` |
| Propose vs Approve as “two write paths” | **Not dual** | Architecture §4.4 durable command protocol: Propose performs **zero** provider ops; only Approve after human evidence may open MCP |
| Update `success:true` vs SOQL reconcile → Completed | **Not dual product path** | §4.4 recovery after durable `Invoking` fence — provider is authority; not a second product sentence |
| AppHost `WithSalesforce` vs bare `AddModule<SalesforceModule>` | **Layer / test split, not dual product** | Product AppHost: single sentence `AddModule<SalesforceModule>(…WithSalesforce())`. Integrations L1 uses bare module + scripted MCP edge (honest Built = scripted). Selecting without hosting package remains valid if config wired elsewhere (Agent 49 graph residual) |
| Runtime `McpServerDefinition` vs hosting `McpProviderHostingDefinition` | **Layer split, not dual product path** | Shared keys (`salesforce`, `DigitalBrain:Salesforce`); AppHost projects OAuth params; silo owns endpoint/scopes/tools — intentional |
| Google + Salesforce both call `McpRuntimeHosting.Configure` | **Shared infra, not SF dual** | Soft residual already Agent 40/49: Integrations.Mcp `AddHttpClient` not `TryAdd` → **G2 81–88** |
| `AccountEnrichmentSurface` vs `AccountEnrichment` process | **Already separated** | Surface = OS scene only (opens enrichment scene; no Gmail→SF). Process sample owns Propose/Approve. Architecture §4.4 / compositions honesty — **G4**, not SF package dual |
| Host / Mcp northbound invents SF tools | **No** | `hosts/DigitalBrain.Mcp` has zero Salesforce vocabulary; Host silo has no SF hand-wire |
| Public receipt omits internal `Invoking` | **Not dual product path** | Internal `MutationStatus` has Invoking fence; public `SalesforceMutationState` maps non-terminal to `AwaitingApproval`. Receipts after Approve complete are terminal (`Completed` / `OutcomeUncertain`). Soft docs honesty only (G6 if protocol diagram confuses) |

### Call path (codegraph — single write door)

1. `ISalesforce.ProposeAccountDescription` → durable `AwaitingApproval` receipt (**no MCP session**)
2. Human session emits `SalesforceMutationApproval` + durable delivery evidence
3. `ISalesforce.ApproveAccountDescription` validates evidence/fingerprint/owner session → admit `updateSobjectRecord` + `soqlQuery` schema fingerprints → durable `Invoking` save → `CallAsync(update)` → on failure/uncertain leave, bounded SOQL reconcile → terminal receipt
4. Consumers: sample `AccountEnrichment` + Integrations L1 harness only (no second product writer)

### Product AppHost sentence (single)

```csharp
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
```

No second hand-wired `DigitalBrain:Salesforce:*` projection outside `WithSalesforce`. Silo Host Program.cs has **no** Salesforce domain knowledge.

### Ownership grill (13 — condensed)

1. **What does it do?** Account-description mutation neuron over official hosted MCP with human approval + reconcile.  
2. **Architecture align?** Yes — §4.4 command protocol, evidence gate, Invoking fence, no Tasks dependency.  
3. **Belong here?** Yes — vocabulary on Contracts; policy/admission/update on runtime; transport on Integrations.Mcp; OAuth projection on optional Aspire.Hosting.  
4. **Public surface?** Contracts: `ISalesforce` + mutation/approval types. Runtime: `SalesforceModule` only. Hosting: `WithSalesforce` only.  
5. **Hide SDK?** Yes — MCP client types only inside internal Invoke/Approve callbacks; no direct PackageReference on SF family.  
6. **Dual path?** **None residual** for product write/hosting sentences.  
7. **Wrong layer?** No — tool names and schemas stay private on runtime (not Contracts).  
8. **Hosting?** Optional package correct (Agent 49); product AppHost uses it once.  
9. **Depend AI/Tasks?** No — compose at application/sample layer.  
10. **Designed absences?** Auto-approve classification + Task parking on `OutcomeUncertain` — **Designed unbuilt**; do not invent.  
11. **Trash delete?** Nothing foldable dual left in SF product packages.  
12. **New public API?** None this cycle.  
13. **Family exit?** **Not this agent.**

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Salesforce/DigitalBrain.Modules.Salesforce.csproj -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/...csproj -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~ResidualPackageGraphContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~HostingProjectionContracts"
→ Passed: 70, Failed: 0
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm **not claimed**. Integrations.Tests not claimed by this agent (foreign peer WIP on `SalesforceMutation.cs`).

### Residuals (honest, not fake green)

| Residual | Status | Owner |
| --- | --- | --- |
| Live OAuth / hosted MCP outside scripted L1 | open | G1 remaining SF peers / G3 / G7 live |
| Exact tool admission stays module-owned | **protected** | runtime `Invoke` Select*Tool |
| Auto-approve classification; Task parking on `OutcomeUncertain` | Designed — **unbuilt** | do not invent (architecture §4.4) |
| Soft string couple hosting key ↔ runtime `McpServerDefinition` / sample `"salesforce"` grain key | intentional convention | soft honesty; G4 sample key const optional |
| `McpRuntimeHosting.AddHttpClient` double-call when Google+SF select | soft Integrations.Mcp | **G2 81–88** |
| Public state omits `Invoking` (internal fence) | soft protocol honesty | G6 docs if needed — not a product dual fold |
| Peer Integrations.Tests SF fingerprint facts WIP | foreign concurrent | do not reverse; G5/SF peers own |
| Full G1 Salesforce family exit | **not this agent** | remaining peers + docs-honesty closer |

### Verdict

Salesforce product surface has **zero residual dual product paths**. Propose/Approve is the ratified single mutation rail; MCP is the only southbound transport; AppHost uses one `WithSalesforce` sentence; recovery reconcile is not a second writer. Success = assessed + residuals listed — **no product C# edit**. Agent 51 scorecard notes only; root gate unclaimed; **not** a full Salesforce family exit.

*Agent 51 mid-band complete. Peers: Agent 46 Contracts + Agent 49 package graph/`SalesforceModule` G1-clean. Family exit still open.*

---


## Wave G1 Salesforce family exit (agents 46–52) — **COMPLETE with honest residuals**

**Mission (Agent 52):** `docs-honesty`  
**Exit criteria (prompt §7 G1):** *ships neurons/synapses? hides SDK? hosting optional package correct?*  
Salesforce-specific: **ships `ISalesforce` + mutation vocabulary?** · **hides MCP/tools/OAuth?** · **optional Aspire.Hosting `WithSalesforce` correct?** · residuals: **live cloud not default L1** · **auto-approve classification Designed** · **Task `OutcomeUncertain` parking Designed**.

**Numbering note:** prompt §7 table lists Salesforce as agents **49–56**. Orchestrator assigned this family exit as agents **46–52** (Agent 52 = docs-honesty closer). Scorecard uses the orchestrator band. Mid-band peers **49** (package graph/`SalesforceModule`) and **51** (residual dual path) are preserved above.

Quoted at finalize (Agent 52, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 52 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- modules/DigitalBrain.Modules.Salesforce modules/DigitalBrain.Modules.Salesforce.Contracts modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting
(empty — Salesforce product packages clean)
```

**Foreign dirty (Agent 52 did not author product C#; Salesforce product packages clean):** concurrent AI/Time/Kernel/packages.md WIP; Integrations.Tests `SalesforceMutation.cs` + `GmailReadMessage.cs` + untracked `GoogleContracts.cs`; Boundary/Time/Tasks test pins; this scorecard. **Do not reverse foreign dirty.**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `tests/.../SalesforceMutation.cs` | Concurrent peer: CommandId fingerprint reuse reject + mismatched-fingerprint approve reject facts | **G1 Salesforce L1 honesty** — foreign; left unstaged by Agent 52 |
| AI / Time / Kernel / Google test WIP | Concurrent other G1 bands | **Do not reverse** |
| Agent 49 / 51 mid-band scorecard blocks | Peers wrote package-graph + dual-path assessments | Folded into this exit |
| This scorecard | Multi-agent campaign record + Agent 52 exit | Campaign record |

Agents 46–51 folded from peer mid-band (46 Contracts; 49 package graph; 51 dual-path) + on-disk re-proof (runtime encapsulate, hosting, architecture §4.4 / packages.md). **No concurrent product C# under `modules/**/Salesforce*`.**

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships neurons / synapses | **PASS** | **Contracts** public surface only: `ISalesforce` (`ProposeAccountDescription` / `ApproveAccountDescription`), receipt `SalesforceAccountDescriptionMutation` + `SalesforceMutationState` (`AwaitingApproval` / `Completed` / `OutcomeUncertain`), approval synapse `SalesforceMutationApproval` (`: Synapse`). Namespace `DigitalBrain.Salesforce`. Contracts csproj: **Abstractions-only** (+ SourceGeneration analyzer, `PrivateAssets=all`). Zero PackageReference. No tool names, OAuth types, MCP types, or Kernel types on contracts. |
| Hides SDK / transport / tools | **PASS** | **Runtime** public product surface = **`SalesforceModule` only**. Neuron `internal sealed partial class Salesforce : Neuron, ISalesforce`. Internal: `MutationData` / `MutationStatus` (includes **Invoking** fence not on public receipt enum), propose/approve/invoke/reconcile/state, tool names `updateSobjectRecord` / `soqlQuery`, endpoint URI, OAuth durable keys, schema admission, reconciliation. Runtime csproj: Contracts + Integrations.Mcp + Kernel — **zero** direct PackageReference (MCP SDK owned by Integrations.Mcp; `ModelContextProtocol.Client` only inside **internal** `Invoke.cs`). Boundary pin `McpProvidersDependOnSharedMechanics` forbids direct `ModelContextProtocol.Core` / DataProtection / Http on provider runtimes. **No** Tasks/AI/Google project refs. Integrations.Mcp IVT friend only. Agent 51: zero residual dual product paths (Propose→Approve is single rail; SOQL reconcile is recovery, not second writer). |
| Hosting optional package correct | **PASS** | Separate packable `DigitalBrain.Modules.Salesforce.Aspire.Hosting`: public `WithSalesforce` only; OAuth projection via `McpProviderHosting.Register`. Hosting csproj: Salesforce runtime + Integrations.Mcp.Aspire.Hosting. AppHost uses `AddModule<SalesforceModule>(… => WithSalesforce())`; Integrations L1 uses `AddModule<SalesforceModule>()` **without** hosting (scripted edge) — optional correct. No Auto. Agent 49: three-pack graph matches packages.md (Google twin). |
| Residual holds (not fake green) | **PASS** | Live cloud **not** default L1; auto-approve **Designed**; Task parking **Designed** (`AttemptOutcomeUncertain` has no producer); root gate **not claimed** |

### Package role map (re-proof)

| Package | Direct ProjectReference (compile) | Public product surface | Must stay out |
| --- | --- | --- | --- |
| `DigitalBrain.Modules.Salesforce.Contracts` | Abstractions | `ISalesforce`, `SalesforceAccountDescriptionMutation`, `SalesforceMutationState`, `SalesforceMutationApproval` | MCP SDK, tool dictionaries, OAuth, Kernel, Integrations |
| `DigitalBrain.Modules.Salesforce` | Contracts, Integrations.Mcp, Kernel | `SalesforceModule` only; neuron/impl **internal** | Public neuron; Tasks/AI edges; direct MCP NuGet |
| `DigitalBrain.Modules.Salesforce.Aspire.Hosting` | Salesforce runtime, Integrations.Mcp.Aspire.Hosting | `WithSalesforce` projection | Runtime mutation logic; secrets on non-silo refs |

**deps.json (Release) direct deps:** Contracts → Abstractions; Runtime → Integrations.Mcp + Kernel + Salesforce.Contracts; Aspire.Hosting → Integrations.Mcp.Aspire.Hosting + Salesforce. **packages.md match:** yes.

### Public vs internal protocol honesty

| Layer | States |
| --- | --- |
| Architecture §4.4 durable protocol | `AwaitingApproval` → `Invoking` → `Completed` \| `OutcomeUncertain` |
| Public `SalesforceMutationState` | `AwaitingApproval`, `Completed`, `OutcomeUncertain` only |
| Internal `MutationStatus` | includes **`Invoking`** (durable fence before provider write) |

`Receipt(...)` maps non-terminal internal statuses (including `Invoking`) to public `AwaitingApproval`. **Deliberate encapsulation** of the intermediate fence — architecture still describes Invoking; public receipt folds it. Do not publicize `Invoking` without product author need.

### What G1 Salesforce does *not* claim (anti-fake-green)

- Live Salesforce cloud / real OAuth L1 as Built default — **scripted southbound MCP edge** only
- Operation auto-approve classification — §4.4 **“Ratified but not built”**; single mutating op always demands human evidence
- Approver-agent-may-advise-but-never-authorizes as Built machinery
- Parking owning Task on `OutcomeUncertain` / product `AttemptOutcomeUncertain` producer — **Designed**; no Tasks package reference by construction
- Exactly-once external effect — architecture rejects the claim
- Root `dotnet build|test DigitalBrain.slnx` / docs npm green (Agent 52 — **not run / not claimed**)
- That Agent 52 authored Salesforce product or L1 test C# — **scorecard only**

### Holds after Salesforce family grill

| # | Hold | Status after 46–52 | Residual recommendation |
| --- | --- | --- | --- |
| — | Live OAuth / hosted MCP outside scripted L1 | **open (honest residual)** | Keep Built = scripted edge; live only with product host + Explicit/live tests |
| — | Auto-approve operation classification + agent-advise-not-authorize | **Designed — protect absence** | Do not invent auto-approve Built |
| — | Task parking on `OutcomeUncertain` | **Designed — protect absence** | No producer; caller refuses non-`Completed`; G4/G5/G6 honesty |
| 10 | Integrations.Mcp IVT friend names | **still soft (G2)** | IVT friendship not vocabulary — leave |
| — | `McpRuntimeHosting.AddHttpClient` double-call when Google+Salesforce both activate | **soft Integrations.Mcp** | **G2 81–88** (Agent 40/49 residual) |
| — | Public receipt omits `Invoking` | **closed as deliberate hide** | Keep internal fence |

**Closed / protected this family:** MCP/tool/OAuth on Contracts; publicizing `Salesforce` neuron or `MutationData`; Salesforce→Tasks package edge; inventing auto-approve Built; claiming live cloud from unit green; dual REST/SOAP path; Auto hosting; dual product doors for Propose vs Approve.

### Peer summary (agents 46–51 → 52)

| Agent band | Focus | Folded result |
| --- | --- | --- |
| 46 | Contracts deep (`contract-surface`) | **G1-clean:** `ISalesforce` + receipt + approval synapse; Abstractions-only; no MCP/tool/OAuth |
| 47–48 | Runtime encapsulate | `Salesforce` internal; propose zero-MCP; approve evidence; Invoking fence + reconcile; tools internal; zero Tasks/AI |
| 49 | Package graph + `SalesforceModule` | **G1-clean mid-band** (scorecard block above); three-pack = packages.md; hosting optional |
| 50 | Hosting / L1 | Concurrent Integrations L1 fingerprint facts (foreign) |
| 51 | Residual dual path | **G1-clean mid-band** (scorecard block above); zero dual product paths |
| 52 | Docs-honesty exit | This block; residual map + cycle log; foreign WIP surfaced; root gate unclaimed |

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Salesforce.Contracts -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Salesforce -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~ResidualPackageGraphContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~HostingProjectionContracts"
→ Passed: 70, Failed: 0

dotnet test tests/DigitalBrain.Integrations.Tests -c Release --filter "FullyQualifiedName~SalesforceMutation"
→ Passed: 6, Failed: 0
```

Line-count (product Salesforce `*.cs`, excl bin/obj): max **298** `Invoke/Invoke.cs` — under 400. Root slnx / docs npm **not claimed**.

### Grill board (§2) — Agent 52 condensed

1. **What does it do?** Account-description mutation neuron: propose receipt, human-approve with evidence, admit tools, write, reconcile uncertainty.  
2. **Consumers today?** AccountEnrichment sample; AppHost; Integrations L1; HostingProjection pins.  
3. **Architecture place?** §4.4 Built + Designed residuals — not silent invent.  
4. **Kind?** Module vocabulary + runtime logic + optional hosting projection.  
5. **Public that should be internal?** None material — neuron/tools/Invoking already internal.  
6. **Delete impact?** Breaks CRM mutation path + enrichment sample + AppHost `WithSalesforce`.  
7. **Contracts leak SDK?** No.  
8. **Kernel domain word?** No.  
9. **Invent Behavior / IReminder / Auto?** No.  
10. **Claimed without command?** Root gate explicitly unclaimed; scoped build/test quoted.  
11. **Foreign dirty?** AI/Time/Kernel + concurrent Integrations Gmail/Salesforce/Google tests — surfaced, not reversed.  
12. **Layer move?** No — graph twin of Google already correct.  
13. **New engineer via architecture alone?** Yes — §4.4 + packages.md three-pack match disk.

### Verdict

Salesforce family **ownership aligns** with architecture §4.4 and packages.md for Built scripted-MCP Account mutation surface. Success = assessed + Designed residuals honest (auto-approve, Task parking, live cloud) — **not** inventing live Built-cloud or Tasks coupling. Agent 52 wrote scorecard only; root gate unclaimed.

*End Wave G1 Salesforce family (agents 46–52). Agent 52 wrote scorecard only. Root gate not claimed.*

---

## Wave G1 Flutter — Agent 54 (runtime encapsulate) — **G1-clean, not family exit**

**Mission:** `encapsulate`  
**Write scope:** `modules/DigitalBrain.Modules.Flutter/**` runtime only.  
**Question:** Are `ShellNeuron` / `SceneNeuron` internal? Is implementation hidden behind contracts + `FlutterModule`?

**Not this agent:** full Flutter family exit; Contracts deep surface grill; Aspire.Hosting `WithUiEdge`/`WithFlutterHost` mid-band; Built-live topology / product Healthy; chrome beyond key/title; product journal observation on `IDigitalBrain`.

Quoted at mid-band (Agent 54). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 54 mid-band

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- modules/DigitalBrain.Modules.Flutter
(empty — Flutter runtime product package clean)
```

**Foreign dirty (Agent 54 did not author product C#; Flutter runtime clean):** concurrent AI/Time/Kernel/packages.md WIP; Integrations.Tests Gmail/Salesforce + untracked GoogleContracts; Boundary/Time/Tasks test pins; concurrent Flutter peers (Agent 59 dual-path scorecard); this scorecard. **Do not reverse foreign dirty.**

### Source inventory (hand-authored runtime `*.cs`, excl bin/obj)

| File | Visibility | Role |
| --- | --- | --- |
| `FlutterModule.cs` | `public sealed partial class FlutterModule : IModule` | Selection marker for `AddModule<FlutterModule>` — **keep public** |
| `ShellNeuron.cs` | `internal sealed class ShellNeuron : Neuron, IShell, IEmit<SceneOpened>` | GrainType `"shell"`; `Open(OpenScene)` → emit `SceneOpened` |
| `SceneNeuron.cs` | `internal sealed class SceneNeuron : Neuron, IScene, IHandle<ControlActivated>` | GrainType `"scene"`; admit/validate `ControlActivated` |

No other runtime source files. No provider SDK, no Dart/Flutter types, no Ui edge types, no hosting projection types in this package.

### PE oracle (Release runtime DLL — MetadataLoadContext)

Assembly: `DigitalBrain.Modules.Flutter` (Release `net10.0`).

| Visibility | Type |
| --- | --- |
| **Public (exported)** | `DigitalBrain.Flutter.FlutterModule` **only** |
| NotPublic | `DigitalBrain.Flutter.ShellNeuron` |
| NotPublic | `DigitalBrain.Flutter.SceneNeuron` |
| NotPublic | `DigitalBrain.Generated.DispatchManifest` (source-gen) |
| NotPublic | `OrleansCodeGen.DigitalBrainModulesFlutter.Metadata_DigitalBrainModulesFlutter` |

**PASS:** runtime public product surface = **`FlutterModule` only**. Neuron implementations are not exported.

### Package graph (runtime only)

| Compile ProjectReference | Role |
| --- | --- |
| `DigitalBrain.Modules.Flutter.Contracts` | Vocabulary (`IShell`/`IScene` + facts) |
| `DigitalBrain.Kernel` | `Neuron` base |
| `DigitalBrain.SourceGeneration` (analyzer, non-compile, `PrivateAssets=all`) | Module/dispatch generation |

deps.json top-level DigitalBrain graph: Abstractions, Kernel, Flutter.Contracts — **no** Dart/Flutter SDK assemblies, **no** Ui host, **no** Aspire hosting package (hosting is sibling packable package — correct optional OS surface).

### Consumers address contracts, not impl

| Consumer class | Addresses |
| --- | --- |
| `DigitalBrain.Flutter.Tests` L1 (`ShellSceneRoundTrip`) | `IShell` / `IScene` + `OpenScene` / `SceneOpened` / `ControlActivated` via `TestBrain` |
| Ui edge / compositions / AppHost | `FlutterModule` selection + Contracts vocabulary |
| Boundary pins (`FlutterContracts`) | Contracts exported first-five vocabulary only — **no** runtime export pin yet (see residual) |

No test or host `typeof(ShellNeuron)` / `typeof(SceneNeuron)`. **No `InternalsVisibleTo` required** for product L1.

### Encapsulate answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| `ShellNeuron` internal? | **PASS** | Source `internal sealed`; PE NotPublic |
| `SceneNeuron` internal? | **PASS** | Source `internal sealed`; PE NotPublic |
| Hide impl / only module marker public? | **PASS** | PE exported types = `[FlutterModule]` only |
| No Dart/Flutter SDK on runtime? | **PASS** | csproj ProjectReferences Contracts+Kernel only; no PackageReference SDK |
| Product C# change required? | **No** | Family already ownership-aligned; inventing renames/partials would be trash |

### Residuals (honest — not fake green)

| Residual | Status | Owner |
| --- | --- | --- |
| Optional PE export pin (`RuntimePublicSurfaceIsModuleMarkerOnly` twin of Time) | **Absent** on Flutter — L0 pins Contracts vocabulary only (`FlutterContracts.PublicVocabularyIsFirstVerticalSurfaceOnly`) | Optional G1 Flutter peer / G5 — **not** Agent 54 write scope (`tests/**`) |
| Built-live product AppHost Healthy (`digitalbrain-ui` + Flutter host) | **Still open** (Hold #6) | G1 Flutter peers + G3 Ui + G7 |
| Product journal observation on `IDigitalBrain`; chrome beyond key/title | **Designed** | Do not invent as Built |
| Aspire.Hosting public surface grill | Peer mid-band | Agent 55 / hosting peers |
| Root `dotnet build|test DigitalBrain.slnx` / docs npm | **Not claimed** | G7 |

### Scoped verify (Agent 54 — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Flutter -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Flutter.Tests -c Release
→ Passed: 2, Failed: 0

dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~FlutterContracts"
→ Passed: 3, Failed: 0
```

### What Agent 54 does *not* claim

- Full Flutter family exit (Contracts + hosting + docs-honesty closer still open)
- Built-live OS topology Healthy
- That a PE export pin test already exists for Flutter runtime (it does not — optional residual)
- Root gate green
- Authorship of Flutter product C# this cycle — **scorecard only**

### Verdict

Flutter **runtime** encapsulation is already correct and PE-proven: consumers program `IShell`/`IScene`; grains `ShellNeuron`/`SceneNeuron` stay assembly-internal; public runtime surface is the `FlutterModule` selection marker only. Success = assessed + residuals listed — **no product C# edit**. Agent 54 scorecard notes only; root gate unclaimed; **not** a full Flutter family exit.

*Agent 54 mid-band complete. Peers: Agent 59 dual-path G1-clean. Family exit closed by Agent 64.*

---

## Wave G1 Flutter — Agent 59 (Ui hand-wire vs With* residual dual path) — **G1-clean, not family exit**

**Mission:** `own-audit`  
**Write scope:** Flutter residual dual path **Ui hand-wire vs `With*`**; residual scorecard Flutter mid notes.  
**Not this agent:** full Flutter family exit; invent `IFlutter` god; live product AppHost Healthy claim; chrome beyond key/title; product journal observation on `IDigitalBrain`; product C# fold unless dual found.

Quoted at Agent 59 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 59

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status --porcelain -- modules/DigitalBrain.Modules.Flutter* hosts/DigitalBrain.Ui hosts/DigitalBrain.AppHost/AppHost.cs hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj
(empty — Flutter product packages, Ui host, and product AppHost composition paths clean)
```

**Flutter / Ui / product AppHost porcelain at audit start:** **clean**. Mid-session foreign concurrent WIP appeared under `Flutter.Aspire.Hosting` (narrow `FlutterHostLaunch` members `public`→`internal`/`private`; drop host-kind narrative comments on `DesktopHost`) — **not authored by Agent 59**; **do not reverse**. Broader foreign dirty: AI, Time, Kernel, Integrations tests, packages.md, this scorecard.

### Disk inventory (product Flutter OS composition surface)

| Path | Role |
| --- | --- |
| `Flutter.Contracts` | Public first-five: `IShell`, `IScene`, `OpenScene`, `SceneOpened`, `ControlActivated` — **no `IFlutter`** |
| `Flutter/ShellNeuron.cs`, `SceneNeuron.cs` | `internal` neurons |
| `Flutter/FlutterModule.cs` | Public selection marker only |
| `Flutter.Aspire.Hosting/FlutterHostingExtensions.cs` | Sole product AppHost OS surface projector: `WithUiEdge` / `WithFlutterHost` / `WithFlutterHost<T>` |
| `Flutter.Aspire.Hosting/FlutterHostLaunch.cs` | Desktop vs Headless launch resolve (explicit; **no Auto**) |
| `hosts/DigitalBrain.Ui/*` | Northbound edge executable (`MapUiHost` → routes); **not** an AppHost composition sentence |
| `hosts/DigitalBrain.AppHost/AppHost.cs` | Single product sentence: `.WithUiEdge().WithFlutterHost()` |
| `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` | ProjectReference **Flutter.Aspire.Hosting** only — **no** direct `DigitalBrain.Ui` resource ref |

### Dual-path grill (what counts as dual for this mission)

| Candidate dual | Present? | Verdict |
| --- | --- | --- |
| AppHost hand-wire `AddProject`/`Projects.DigitalBrain_Ui` beside `WithUiEdge` | **No** | Product AppHost never references Ui as Aspire project resource; only path-based `AddProject` lives **inside** `EnsureUiEdge` |
| Second product AppHost sentence (free-floating Ui / Flutter executable) | **No** | Grep all `AppHost.cs`: only product uses Flutter module; TestingAppHost/Quickstart omit OS surface by design |
| `AddModule<FlutterModule>()` silently starts Ui/Flutter | **No** | Vocabulary-only L0: no `digitalbrain-ui` / `digitalbrain-flutter` without `With*` |
| `WithFlutterHost` without edge as orphan host | **No** | `EnsureFlutterHost` implies `WithUiEdge()` when edge missing |
| Desktop vs Headless as dual product doors | **Not dual** | Explicit host kinds (same style as AI `WithLlm<T>`); default Desktop; missing markers **throw** — architecture forbids Auto |
| `MapUi` vs `MapUiHost` | **Layer split, not dual product path** | Host-internal: `MapUiHost` = health + `MapUi` routes; single Program composition; L1 tests exercise host path in-process |
| In-process L1 `MapUiHost` vs AppHost `WithUiEdge` projection | **Test vs product layer, not dual product** | L1 proves edge without Aspire graph; L0 proves graph projection — both point at same `hosts/DigitalBrain.Ui` |
| `FlutterHostingExtensions.UiHealthPath` vs `UiEdgeContract.HealthPath` | **Soft packaging couple** | Both `"/health"`; packable hosting must not depend on host project — intentional string couple; optional G3 lockstep pin if risk |
| MCP host uses `FlutterHostingExtensions.OwnerEnvironmentVariable` | **Shared owner pin, not Ui dual** | MCP remains AppHost-owned peer; does not project Ui |
| Public `IFlutter` mega-neuron | **Absent — protected** | Contracts inventory = first-five only; architecture §4.6 must-not-return |
| Kernel / MCP / Orleans client as Flutter env | **No** | Host exclusive env = `DIGITALBRAIN_UI_BASE` + `DIGITALBRAIN_SHELL` only (L0) |

### Product AppHost sentence (single)

```csharp
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
    .WithFlutterHost());
```

No second hand-wired `digitalbrain-ui` / Flutter `AddExecutable` outside `Flutter.Aspire.Hosting`. Silo Host `Program.cs` has **no** Flutter domain knowledge. Companion AppHosts cannot project or hand-wire OS surface (L0 pin: product has `Flutter.Aspire.Hosting`, lacks direct `Ui`; Quickstart/TestingAppHost reach neither).

### Call path (northbound — single door)

```text
Flutter/Dart host  → HTTP/SSE → hosts/DigitalBrain.Ui (MapUiHost)
  → IDigitalBrain / host-private journal poll → silo (+ FlutterModule when selected)
```

AppHost materializes Ui via **only** `WithUiEdge` (path-resolved csproj + `brain.AsClient()` + owner env). Flutter process materializes via **only** `WithFlutterHost` (edge base + shell env; WaitFor Ui).

### Ownership grill (13 — condensed)

1. **What does it do?** OS surface vocabulary + optional Aspire projection of Ui edge + Desktop/Headless host.  
2. **Architecture align?** Yes — §4.6 Built projection + L0; live Healthy residual honest.  
3. **Belong here?** Yes — module hosting owns composition; Ui host is edge executable; Dart under `clients/`.  
4. **Public surface?** Contracts first-five; runtime `FlutterModule`; hosting `WithUiEdge`/`WithFlutterHost`/`DesktopHost`/`HeadlessHost`/options. **No `IFlutter`.**  
5. **Hide SDK?** Flutter/Dart stay out of C# contracts; hosting does not reference Kernel.  
6. **Dual path?** **None residual** for AppHost Ui hand-wire vs `With*`.  
7. **Wrong layer?** Soft health-path string couple hosting↔host only.  
8. **Hosting?** Optional package correct; product AppHost uses it once for OS surface.  
9. **Depend MCP as UI bus?** No — MCP peer separate.  
10. **Designed absences?** Live topology Healthy; product journal observation; full chrome; multi-principal IdP — **do not invent**.  
11. **Trash delete?** Nothing foldable dual left for this mission.  
12. **New public API?** None this cycle — **did not invent `IFlutter`**.  
13. **Family exit?** **Not this agent.**

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Flutter.Aspire.Hosting/...csproj -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~FlutterHosting"
→ Passed: 10, Failed: 0
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm / live Aspire Desktop Healthy **not claimed**.

### Residuals (honest, not fake green)

| Residual | Status | Owner |
| --- | --- | --- |
| Hold #6 product AppHost OS-surface Healthy / live `aspire start` topology | open | G3 Ui + G7 live — never promote L0 to Built-live |
| Soft `UiHealthPath` ↔ `UiEdgeContract.HealthPath` string couple | intentional packaging | optional G3 pin if product risk |
| Product journal observation on `IDigitalBrain` | Designed — unbuilt | do not invent ProbeHost-shaped watch |
| Full product chrome beyond key/title shell | Designed | clients/shell polish — not dual path |
| Multi-principal IdP edge | Designed | G3 Ui |
| Full G1 Flutter family exit (ships neurons? hide SDK? hosting optional?) | **not this agent** | remaining 57–64 peers + docs-honesty closer |
| Concurrent foreign WIP (AI/Time/Kernel/Integrations tests; mid-session Flutter.Aspire.Hosting visibility narrow) | foreign | do not reverse |

### Verdict

Flutter OS surface composition has **zero residual dual product paths** for Ui hand-wire vs `With*`. Product AppHost uses one `WithUiEdge().WithFlutterHost()` sentence; Ui materialization is owned solely by `Flutter.Aspire.Hosting`; companions cannot hand-wire the surface; `IFlutter` remains correctly **absent**. Soft residuals (health-path string couple, Hold #6 live Healthy) are honesty — not foldable duals. Success = assessed + residuals listed — **no product C# edit**. Agent 59 scorecard notes only; root gate unclaimed; **not** a full Flutter family exit.

*Agent 59 mid-band complete. Family exit closed by Agent 64.*

---

## Wave G1 Flutter — Agent 63 (boundary tests Flutter/Ui ownership) — **G1-clean, not family exit**

**Mission:** `test-contract`  
**Write scope:** `tests/DigitalBrain.Tests/Flutter/**` (Flutter/Ui ownership boundary pins under DigitalBrain.Tests)  
**Not this agent:** full Flutter family exit; product C#; live AppHost Healthy; invent `IFlutter`/Auto; root gate.

Quoted at Agent 63 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 63

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status --porcelain -- tests/DigitalBrain.Tests/Flutter/
 M tests/DigitalBrain.Tests/Flutter/FlutterContracts.cs
```

**Authored this cycle:** only `tests/DigitalBrain.Tests/Flutter/FlutterContracts.cs` (322 physical lines, under 400).  
**Foreign concurrent WIP (do not reverse):** Agent 54/59 scorecard mid-bands; Agent 58-band `tests/DigitalBrain.Flutter.Tests/FlutterVocabulary.cs` + ShellSceneRoundTrip; Flutter.Aspire.Hosting visibility narrow; AI/Time/Kernel/Integrations/packages.md.

### Gap found (test-contract)

Prior `FlutterContracts` only pinned first-five vocabulary + wire golden + assembly SDK refs. Missing ownership sentences already proven for Time/Tasks/Google families:

| Missing pin | Product sentence |
| --- | --- |
| Runtime export surface | `FlutterModule` only — `ShellNeuron`/`SceneNeuron` internal |
| Contracts compile graph | Abstractions-only; no Kernel/Ui/hosting/peer modules/SDK |
| Runtime compile graph | Kernel + Contracts; never Ui/Client/Aspire/peer modules |
| Hosting public surface | Desktop/Headless + options + extensions — **no Auto** |
| Hosting compile graph | Aspire.Hosting + Flutter runtime; does not ProjectReference Ui |
| Ui host edge ownership | client + Aspire + Flutter.**Contracts** only — never Flutter **runtime**/Kernel/hosting/southbound |
| Method/god absence | `IShell.Open` shape; `IFlutter`/`IUiGateway`/`AutoHost` absent |

Hosting L0 (`FlutterHosting*`) and `HostingPackageBoundaryContracts` already covered projection/graph peers; Agent 63 closes the **Flutter folder** test-contract hole so family ownership is witnessable from one suite. Complementary to L1 `Flutter.Tests` vocabulary (foreign peer) — graph pins stay in DigitalBrain.Tests.

### Action

Strengthened `tests/DigitalBrain.Tests/Flutter/FlutterContracts.cs` to pin vocabulary + wire golden + runtime hide + three package graphs (Contracts / runtime / Aspire.Hosting) + northbound Ui edge ownership. No product C#; no dual-path invent; no Built-live claim.

### Assess (template §6)

```
Scope: tests/DigitalBrain.Tests/Flutter/FlutterContracts.cs
What it does: boundary witnesses that Flutter family + Ui host live in architecture homes
Consumer today: CI / DigitalBrain.Tests root gate; ownership campaign
Architecture home: §4.6 + packages.md three-pack + host edge purity
Layer: test (contract)
Public surface under test: IShell/IScene/OpenScene/SceneOpened/ControlActivated;
  FlutterModule; FlutterHostingExtensions+options+Desktop/Headless; Ui package graph
Implementation hidden? Y — neurons/host launch internal; pins prove it
Belongs here? Y — DigitalBrain.Tests owns cross-package graph witnesses
Aligns with modules=neurons+synapses? Y
Dual path / god helper? No product dual; tests pin absence of IFlutter/Auto
Delete candidates: none (wire golden protected)
Move candidates: none — L1 vocabulary peer under Flutter.Tests is complementary, not dual
Verify: see below
```

### Verify (scoped — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~FlutterContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~FlutterHosting"
→ Passed: 23, Failed: 0
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm / live Aspire **not claimed**.

### Residuals (honest)

| Residual | Status | Owner |
| --- | --- | --- |
| Hold #6 Flutter not Built-live | open | G3 Ui + G7 |
| Soft health-path string couple hosting↔Ui | intentional | optional G3 |
| Full G1 Flutter family exit (ships? hide? hosting?) | **not this agent** | remaining peers + docs-honesty closer |
| Concurrent Flutter.Tests vocabulary / hosting visibility WIP | foreign | leave; complementary L1 |

### Grill board (13)

1. **What does it do?** Pins Flutter/Ui ownership: first-five vocab, hidden neurons, package graphs, Ui edge contracts-only.  
2. **Consumer today?** DigitalBrain.Tests CI gate.  
3. **Architecture place?** §4.6 + packages.md — yes.  
4. **Layer?** test contract (witness), not product.  
5. **Public that should be internal?** None found — neurons already internal; tests now prove.  
6. **Delete break?** Would lose graph drift detection on Flutter→Ui/SDK leak.  
7. **Contracts SDK leak?** Proven no (compile graph + package refs).  
8. **Kernel domain word?** No Flutter vocab in Kernel (existing AssemblyBoundary); this suite does not reverse.  
9. **Invent Behavior/IFlutter/Auto?** No — pins absence.  
10. **Claimed without command?** Root gate unclaimed; scoped 23/23 quoted.  
11. **Foreign dirty?** AI/Time/Kernel/Integrations + Flutter peer tests/hosting — surfaced, not reversed.  
12. **Layer move?** Keep graph pins in DigitalBrain.Tests; L1 round-trip stays Flutter.Tests.  
13. **New engineer via architecture alone?** Yes after pins — package homes match §4.6.

### Verdict

Flutter/Ui **boundary test-contract is G1-clean** after strengthening `FlutterContracts`: ownership witnesses match Time/Tasks/Google family depth without product C# and without Built-live theater. Success = tests prove product sentences (vocab/edge/graph) — not internals theater. Agent 63 write scope only; root gate unclaimed; **not** full Flutter family exit.

*Agent 63 mid-band complete. Family exit closed by Agent 64.*

---

## Wave G1 Flutter — Agent 62 (Flutter.Aspire.Hosting residual dual product sentence) — **G1-clean mid-band, not family exit**

**Mission:** `host-edge` — any residual dual product sentence **inside** `Flutter.Aspire.Hosting`; fix if safe.  
**Complements Agent 59** (AppHost Ui hand-wire vs `With*` dual door — already G1-clean).  
**Not this agent:** full Flutter family exit; invent Auto / `IFlutter`; live Built-live claim; product C# fold unless dual found.

**Vision restatement:** One module hosting ladder projects OS surface — product AppHost says `WithUiEdge().WithFlutterHost()` Desktop once; host kinds are explicit selection, not second doors.

**Write scope:** `modules/DigitalBrain.Modules.Flutter.Aspire.Hosting/**` dual product sentence residual; scorecard notes.

**Codegraph first:** `FlutterHostingExtensions` public surface = consts + `WithUiEdge` + `WithFlutterHost` / `WithFlutterHost<THost>` + options + `DesktopHost`/`HeadlessHost` markers. `FlutterHostLaunch` internal. Eager peer resources; `Apply` only Ui WaitFor(silo). Auto absent. Product AppHost sole OS surface composer.

### Dual product sentence residual table (hosting API)

| Candidate dual | Verdict | Why |
| --- | --- | --- |
| Product AppHost `WithUiEdge().WithFlutterHost()` vs hand-wired Ui/Flutter resources | **No dual** (Agent 59) | Module API only; companions cannot hand-wire (Selection L0) |
| `WithUiEdge` alone vs `WithFlutterHost` (implies edge) vs both chained | **Composition ladder, not dual door** | §4.6: edge-only is valid selection; host implies edge; product sentence is explicit chain. L0 proves edge-only omits flutter host |
| `WithFlutterHost()` vs `WithFlutterHost<DesktopHost>()` | **Same product sentence** | Non-generic is Desktop sugar; L0 `WithFlutterHostDesktopHostMatchesDefault` |
| `WithFlutterHost()` Desktop vs `WithFlutterHost<HeadlessHost>()` | **Explicit mode selection** | No Auto; fail-closed on missing markers/entry; Headless is CI/pure-Dart alternate |
| `AddModule<FlutterModule>()` without `With*` | **Intentional omit** | Vocabulary-only → no OS resources |
| `UiHealthPath` vs `UiEdgeContract.HealthPath` (`"/health"`) | **Layer value-match — hold** | Packable hosting must not ProjectReference host Ui; not a second composition door |
| `FlutterHostingExtensions.DefaultOwner` / `OwnerEnvironmentVariable` vs `DigitalBrainClientHostingExtensions.DefaultOwner` / `OwnerConfigurationKey` | **Layer dual const — hold** | Env `DigitalBrain__Owner` ↔ config `DigitalBrain:Owner`; both `"dev"`. Collapse is G2 Aspire composition, not host-edge micro-fold |
| AppHost MCP reuses Flutter owner const | **Not dual product path** | Product already refs Flutter hosting; reuses ambient for MCP client owner |
| Historical Auto host | **Already deleted** | Zero Auto symbols; narrative host-kind comments already stripped (foreign concurrent WIP — do not reverse) |

### Product sentence (intact)

```csharp
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
    .WithFlutterHost());
```

Desktop default only on product AppHost — no accidental Headless. Once-only guards on edge and host.

### Soft residuals (honest, not dual product paths)

| Residual | Hold | Attack |
| --- | --- | --- |
| Live product topology Healthy | Hold #6 | G3 Ui / G7 live-aspire |
| Health-path string couple hosting↔Ui host | layer split | G3 if shared edge-const package appears (do not invent) |
| Owner ambient dual Flutter hosting↔Aspire client | layer split | G2 Aspire |
| Foreign concurrent test/scorecard WIP | concurrent peers | leave unstaged; do not reverse |

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Flutter.Aspire.Hosting -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

./tests/DigitalBrain.Tests/bin/Release/net10.0/DigitalBrain.Tests.exe -method "DigitalBrain.Tests.Hosting.*"
→ Total: 11, Failed: 0, Errors: 0
```

(`dotnet test` rebuild of DigitalBrain.Tests was blocked mid-session by foreign concurrent `FlutterContracts.cs` xUnit2029 — Hosting method filter on existing DLL quoted green. Agent 63 later touched that file.)

Line-count product hosting: `FlutterHostingExtensions.cs` **227**, `FlutterHostLaunch.cs` **95** — under 400. Root slnx / docs npm / live Aspire **not claimed**.

### Grill board (§2) — Agent 62

1. **What does it do?** Projects `digitalbrain-ui` (AsClient) + Desktop/Headless Flutter executable when host options selected.  
2. **Consumers today?** Product AppHost; Hosting L0; UiFixture aliases.  
3. **Architecture place?** §4.6 Built projection; not Built-live.  
4. **Kind?** Host-edge projection, not vocabulary/logic.  
5. **Public that should be internal?** Launch resolver already internal; markers/options are selection surface — keep.  
6. **Delete impact?** Breaks product OS composition + Hosting Flutter L0.  
7. **Contracts leak SDK?** N/A (hosting; no Dart/Flutter SDK PackageReference).  
8. **Kernel domain word?** No.  
9. **Invent Behavior / IReminder / Auto?** No.  
10. **Claimed without command?** Root/live unclaimed; scoped build + Hosting 11/11 quoted.  
11. **Foreign dirty?** AI/Kernel/Integrations/Time concurrent WIP; Agents 54/59/63 scorecard peers — not reversed.  
12. **Layer move?** No dual to fold; soft owner/health duals hold to G2/G3.  
13. **New engineer via architecture alone?** Yes — §4.6 product sentence + selection table match disk.

### Verdict

Flutter Aspire Hosting has **zero residual dual product paths**. Composition ladder and Desktop/Headless selection are intentional (architecture-ratified), not dual doors; product sentence remains single Desktop `WithUiEdge`+`WithFlutterHost()`; Auto is gone. Soft layer duals held honestly. Success = assessed + residuals listed — **no product C# edit**. Agent 62 scorecard notes only; root gate unclaimed; **not** a full Flutter family exit.

*Agent 62 mid-band complete. Peers: Agent 54 runtime encapsulate + Agent 59 dual-path + Agent 63 test-contract G1-clean. Family exit closed by Agent 64.*

---

*Agent 63 mid-band complete. Family exit closed by Agent 64 (Flutter 53–64 / prompt 57–64).*

---

## Wave G1 Flutter family exit (agents 53–64) — **COMPLETE with honest residuals**

**Mission (Agent 64):** `docs-honesty`  
**Exit criteria (prompt §7 G1):** *ships neurons/synapses? hides SDK? hosting optional package correct?*  
Flutter-specific: **ships first-five `IShell`/`IScene` vocabulary?** · **hides Dart/Flutter SDK from C# contracts + runtime neurons internal?** · **optional Aspire.Hosting `WithUiEdge`/`WithFlutterHost` Desktop|Headless correct (no Auto)?** · residuals: **Built first vertical ≠ Built-live full chrome** · **product journal observation on `IDigitalBrain` Designed**.

**Numbering note:** prompt §7 table lists Flutter as agents **57–64**. Orchestrator assigned this family exit as agents **53–64** (Agent 64 = docs-honesty closer, filling the band after Salesforce 46–52). Scorecard uses the orchestrator band. Mid-band peers **54** (runtime PE), **59** (Ui hand-wire vs `With*`), **62** (hosting dual sentence), **63** (Flutter/Ui boundary test-contract) are preserved above.

Quoted at finalize (Agent 64, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 64 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- modules/DigitalBrain.Modules.Flutter modules/DigitalBrain.Modules.Flutter.Contracts modules/DigitalBrain.Modules.Flutter.Aspire.Hosting hosts/DigitalBrain.AppHost/AppHost.cs docs/architecture.md
(product AppHost + architecture §4.6 clean; concurrent foreign narrow dirty under Flutter.Aspire.Hosting — see foreign dirty)
```

**Foreign dirty (Agent 64 did not author product C#):** concurrent AI/Time/Kernel/packages.md WIP; Integrations.Tests Gmail/Salesforce + untracked `GoogleContracts.cs`; Boundary/Time/Tasks/ModuleTests pins; Agent 63 FlutterContracts test strengthening; concurrent peer narrow on `Flutter.Aspire.Hosting` (`FlutterHostLaunch` members `public`→`internal`/`private`; drop host-kind narrative comments on `DesktopHost`/`HeadlessHost`) — aligns with hide-implementation, **do not reverse**; this scorecard.

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| AI / Time / Kernel / packages.md WIP | Concurrent other G1 bands + G0-era MEAI honesty | **Do not reverse** |
| `Flutter.Aspire.Hosting` visibility narrow | `FlutterHostLaunch` public→internal/private; host-kind comment delete | Concurrent peer encapsulate — **do not reverse** |
| Integrations / Boundary / Time / Tasks test WIP | Concurrent peer L1/boundary pins | Foreign; left unstaged |
| Agent 54 / 59 / 62 / 63 mid-band scorecard blocks | Peers wrote runtime / dual-path / hosting / test-contract assessments | Folded into this exit |
| This scorecard | Multi-agent campaign record + Agent 64 exit + Wave G1 COMPLETE | Campaign record |

Agents 53–63 folded from peer mid-band (54 runtime PE; 59 dual-path; 62 hosting sentence; 63 test-contract) + on-disk re-proof (contracts first-five, hosting Desktop/Headless, AppHost product sentence, architecture §4.6 / packages.md / site.test.mjs / CLAUDE residual honesty). **No concurrent product C# under `modules/**/Flutter*`** at Agent 64 finalize porcelain for those trees.

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships neurons / synapses | **PASS** | **Contracts** public surface (first vertical, ≤5 types): `IShell` (`Open(OpenScene)`), `IScene`, request `OpenScene`, synapses `SceneOpened` + `ControlActivated` (`: Synapse`). Namespace `DigitalBrain.Flutter`. Contracts csproj: **Abstractions-only** (+ SourceGeneration analyzer, `PrivateAssets=all`) + wire golden `flutter-wire-contracts.golden.json`. Zero PackageReference. No Dart/Flutter types, no `IFlutter` god, no widget/descriptor algebra. Agent 63: boundary pins reaffirm first-five + package graph. |
| Hides SDK / host runtime | **PASS** | **Runtime** public product surface = **`FlutterModule` only** (Agent 54 PE re-proof). Neurons `internal sealed class ShellNeuron` / `SceneNeuron`. Runtime csproj: Contracts + Kernel only — **zero** Dart/Flutter NuGet, zero UI edge host types. Pixel host is **out-of-band** under `clients/` (not a packable module): pure-Dart `digitalbrain_flutter` (`sdk: ^3.12.0` only — no `sdk: flutter` at root) + nested Desktop `shell/` (`digitalbrain_flutter_shell` with `sdk: flutter`). Ui edge (`hosts/DigitalBrain.Ui`) is AsClient HTTP/SSE peer; SSE uses host-private `ISessionNeuron.ReadNeuronJournal` — **not** `IDigitalBrain` journal observation. Grep `modules/**/Flutter*`: no `IFlutter`, no Auto host, no Flutter SDK refs. |
| Hosting optional package correct | **PASS** | Separate packable `DigitalBrain.Modules.Flutter.Aspire.Hosting`: public `WithUiEdge` / `WithFlutterHost()` (= Desktop) / `WithFlutterHost<DesktopHost>()` / `WithFlutterHost<HeadlessHost>()`; markers `DesktopHost`/`HeadlessHost`; options types; product consts. Hosting csproj: Flutter runtime + `DigitalBrain.Aspire.Hosting` only. **No Auto.** `FlutterHostKind` + `FlutterHostLaunch` **internal**. Product AppHost: `AddModule<FlutterModule>(f => f.WithUiEdge().WithFlutterHost())` Desktop default. Vocabulary-only `AddModule<FlutterModule>()` does not start Ui/Flutter resources. Proof tier = L0 projection pins, **not** live Healthy. Agent 59: **zero** residual dual product paths for Ui hand-wire vs `With*`. Agent 62: **zero** residual dual product sentences inside hosting API. |
| Residual holds (not fake green) | **PASS** | Built = **first vertical** only (vocabulary + L0/L1 + projection API + pure-Dart Headless + nested Windows key/title chrome). **Not** Built-live product AppHost topology. Full product chrome beyond key/title **Designed**. Product journal observation on `IDigitalBrain` **Designed** (Hold #7). Multi-principal IdP **Designed**. Root gate **not claimed**. |

### Package role map (re-proof)

| Package | Direct ProjectReference (compile) | Public product surface | Must stay out |
| --- | --- | --- | --- |
| `DigitalBrain.Modules.Flutter.Contracts` | Abstractions | `IShell`, `IScene`, `OpenScene`, `SceneOpened`, `ControlActivated` | Dart/Flutter SDK, widget types, Kernel, `IFlutter` |
| `DigitalBrain.Modules.Flutter` | Contracts, Kernel | `FlutterModule` only; `ShellNeuron`/`SceneNeuron` **internal** | Public neurons; host edge types; Flutter NuGet |
| `DigitalBrain.Modules.Flutter.Aspire.Hosting` | Flutter runtime, Aspire.Hosting | `WithUiEdge` / `WithFlutterHost` / Desktop\|Headless markers + options + product consts | Runtime neuron logic; Auto host; live Healthy claim |

**Line-count (product Flutter `*.cs`, excl bin/obj):** max **229** `FlutterHostingExtensions.cs` — under 400. **packages.md match:** yes (three-pack + OS surface hosting + residual not Built-live).

### Docs honesty re-proof (Agent 64 mission)

| Source | Built claim | Designed / residual | Verdict |
| --- | --- | --- | --- |
| `docs/architecture.md` §4.6 Status line | first-vertical vocabulary + L0/L1 + C# UI edge + `WithUiEdge`/`WithFlutterHost` + pure-Dart headless + nested `shell/` chrome — **code and L0/L1 only** | full chrome beyond key/title; product journal observation on `IDigitalBrain`; multi-principal IdP; **residual unproven** live product topology — **not** Built-live | **Honest** (site.test.mjs pins Status line + Designed parenthetical + Desktop/Headless + AppHost Desktop default) |
| `docs/packages.md` Flutter row + narrative | same first vertical + projection graph shape | Designed chrome/IdP/observation; residual product Healthy — **not** Built-live | **Honest** |
| `CLAUDE.md` §7 stand | first vertical Built (shell/scene, Ui edge, hosting, headless, Windows Material chrome) | full chrome polish, IdP, product journal observation Designed; do not re-open Built Windows chrome as Designed | **Honest** (does not claim Built-live; chrome path less precise than §4.6 `shell/` nesting — not a status lie) |
| `IDigitalBrain` surface | `Get` / `Send` / `Emit` only | no journal watch/read on product client | **Hold #7 still Designed** — Ui uses `ISessionNeuron.ReadNeuronJournal` host-private poll |
| Product AppHost | `WithUiEdge().WithFlutterHost()` Desktop | not Headless by accident; not Auto | **Honest** (Agent 59 dual-path clean) |

### What G1 Flutter does *not* claim (anti-fake-green)

- Product AppHost `aspire start` / `aspire run` Healthy for silo + `digitalbrain-ui` + Flutter host as Built-live
- Full product chrome polish beyond key/title list Material shell
- Multi-principal IdP → owner bind on the Ui edge
- Product journal observation / timeline on `IDigitalBrain` (Hold #7)
- Scene descriptor node algebra / multi-window / theming / `IWindow`
- That `LiveProductUiNorthbound` is a default root-gate proof (`[Fact(Explicit = true)]` — kept Explicit)
- Root `dotnet build|test DigitalBrain.slnx` / docs npm green (Agent 64 — **not run / not claimed**)
- That Agent 64 authored Flutter product C# — **scorecard only**

### Holds after Flutter family grill

| # | Hold | Status after 53–64 | Residual recommendation |
| --- | --- | --- | --- |
| 6 | Flutter not Built-live | **open (honest residual)** — first vertical reaffirmed Built code/L0/L1 | Keep Explicit live; G3 Ui + G7 quote product topology Healthy before any Built-live claim |
| 7 | Product journal observation on `IDigitalBrain` | **Designed — protect absence** | Edge SSE / host-private poll only; do not invent client timeline API without red proof → G2 Client / G3 Ui / G6 |
| — | Full product chrome beyond key/title | **Designed** | Do not re-open Built nested Windows key/title chrome as Designed |
| — | Multi-principal IdP edge | **Designed** | Dev owner config today; production IdP later |
| 13 | Host public const duals | **still soft (G3)** | UiEdgeContract / ProductSurfaceResources spine stays G3; soft health-path string couple hosting↔host intentional |

**Closed / protected this family:** `IFlutter` god; Auto hosting; Dart/Flutter SDK on C# contracts; publicizing `ShellNeuron`/`SceneNeuron`; AppHost free-floating Ui hand-wire dual (Agent 59); hosting dual product sentence (Agent 62); claiming Built-live from L0/L1 unit green; inventing product `IDigitalBrain` journal observation.

### Peer summary (agents 53–63 → 64)

| Agent band | Focus | Folded result |
| --- | --- | --- |
| 53–55 | Contracts deep (`contract-surface`) | **G1-clean:** first-five types; Abstractions-only; wire golden; no `IFlutter`/SDK |
| 54 / 56–58 | Runtime encapsulate | Agent 54 PE: `FlutterModule` only public; neurons internal; Kernel+Contracts graph |
| 59–61 | Hosting / dual path | Agent 59: zero Ui hand-wire dual; Desktop default + Headless; no Auto |
| 62 | Hosting residual dual product sentence | **G1-clean mid-band:** zero residual dual product paths inside hosting API |
| 63 | Boundary tests Flutter/Ui ownership | **G1-clean mid-band:** FlutterContracts graph/export/Ui edge pins |
| 64 | Docs-honesty exit + Wave G1 COMPLETE | This block; residual map; foreign WIP surfaced; root gate unclaimed |

### Verify (scoped — **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Flutter.Contracts -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Flutter -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Flutter.Aspire.Hosting -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~FlutterContracts|FullyQualifiedName~FlutterHosting|FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~ResidualPackageGraphContracts|FullyQualifiedName~HostingPackageBoundaryContracts|FullyQualifiedName~HostingProjectionContracts"
→ Passed: 83, Failed: 0

dotnet test tests/DigitalBrain.Flutter.Tests -c Release
→ Passed: 2, Failed: 0

dotnet test tests/DigitalBrain.Ui.Tests -c Release --filter "FullyQualifiedName!~LiveProductUi"
→ Passed: 9, Failed: 0  (Explicit LiveProductUiNorthbound excluded — residual not Built-live)
```

Root slnx / docs npm **not claimed**.

### Grill board (§2) — Agent 64 condensed

1. **What does it do?** OS surface vocabulary (shell/scene) + optional Aspire projection of Ui edge + Flutter/Dart pixel host.  
2. **Consumers today?** Product AppHost; Ui host; compositions; Flutter/Ui L1; pure-Dart + shell clients.  
3. **Architecture place?** §4.6 Built first vertical + Designed residuals — not silent invent.  
4. **Kind?** Module vocabulary + thin runtime + optional hosting projection + out-of-band clients.  
5. **Public that should be internal?** Neurons already internal; launch/kind internal. Hosting options/consts are deliberate product spine.  
6. **Delete impact?** Breaks OS surface composition + Ui edge projection + host launch.  
7. **Contracts leak SDK?** No.  
8. **Kernel domain word?** No Flutter types in Kernel.  
9. **Invent Behavior / IReminder / Auto?** No.  
10. **Claimed without command?** Root gate explicitly unclaimed; scoped build/test quoted.  
11. **Foreign dirty?** AI/Time/Kernel + concurrent test WIP — surfaced, not reversed.  
12. **Layer move?** No — three-pack + clients/hosts split already correct.  
13. **New engineer via architecture alone?** Yes — §4.6 + packages.md match disk; Built-live residual explicit.

### Verdict

Flutter family **ownership aligns** with architecture §4.6 and packages.md for Built **first vertical** (code/L0/L1 + projection + pure-Dart Headless + nested Desktop key/title chrome). Success = assessed + honest residuals (**not** Built-live; full chrome Designed; product journal observation Designed) — **not** promoting L1 green to live product topology. Agent 64 wrote scorecard only; root gate unclaimed.

*End Wave G1 Flutter family (agents 53–64). Agent 64 wrote scorecard only. Root gate not claimed.*

---

## Wave G1 COMPLETE (agents 17–64) — **all module families closed with honest residuals**

**Closer:** Agent 64 (docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

| Family | Agents (orchestrator) | Exit | Residual one-liner |
| --- | --- | --- | --- |
| AI | 17–24 | COMPLETE | Supervised `IWorker` Designed; MEAI deliberate; `LlmAttribute` internal on WIP |
| Tasks | 25–32 | COMPLETE | Product supervised `IWorker` Designed; no module Aspire.Hosting (correct) |
| Time | 33–36 + 41 | COMPLETE | `ICountdown` only; `IReminder`/recurrence Designed absence protected |
| Google | 37–40, 43–45 | COMPLETE | Live cloud residual; `ICalendar` Designed absence; MCP single path |
| Salesforce | 46–52 | COMPLETE | Live cloud residual; auto-approve + Task parking Designed |
| Flutter | 53–64 | COMPLETE | **Built first vertical; not Built-live full chrome; product journal observation Designed** |

**Wave G1 does *not* claim:** root slnx build/test green; docs npm green; product AppHost OS Healthy Built-live; Behavior rail; calendar Time; Memory; supervised AI Built rewrite.

**Next waves:** G2 cross-cutting (65–96; Client/Abstractions/meta **65–72 COMPLETE**) · G3 hosts (97–128) · G4 samples (129–148) · G5 tests (149–172) · G6 docs honesty (173–188) · G7 full gates (189–200).

*End Wave G1 (agents 17–64). Agent 64 marked WAVE G1 COMPLETE. Root gate not claimed.*

---

## Wave G2 Client + Abstractions + metapackage exit (agents 65–72) — **COMPLETE with honest residuals**

**Exit criteria (prompt §7 G2 cross-cutting / residual map):** programming-model honesty · no Kernel on consumer path · Orleans substrate intentional · no product journal watch on `IDigitalBrain` · metapackage graph clean.

Quoted at finalize (Agent 72, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 72 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status -sb (relevant to this band)
 M docs/packages.md
 M src/DigitalBrain.Abstractions/ISubscriptionRegistry.cs
 M src/DigitalBrain.Client/DigitalBrainClient.cs
 M tests/DigitalBrain.Tests/Packages/ClientApiContracts.cs
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
(+ many foreign concurrent WIP outside Client/Abstractions/meta)
```

**Foreign dirty (Agent 72 did not author product C#):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `src/DigitalBrain.Abstractions/ISubscriptionRegistry.cs` | `[EditorBrowsable(Never)]` | Concurrent G2 peer — Hold #11 soft close on WIP; **do not reverse** |
| `src/DigitalBrain.Client/DigitalBrainClient.cs` | Rejects `Get`/`Send` of `INeuron`/`ISessionNeuron` and `Send` to session grain type | Concurrent G2 peer — hardens Hold #7 (session is gateway, not addressable domain neuron); **do not reverse** |
| `tests/.../ClientApiContracts.cs` (+ residual inventory peers) | Likely pins for session-gate / residual graphs | Concurrent; leave for peer ownership |
| AI / Flutter hosting / Kernel `InvokeAsync` / Time tests / Integrations tests | Concurrent campaign WIP | Surface only |
| This scorecard | Agent 16…72 write scope | Campaign record |
| `docs/packages.md` | MEAI row (G0 foreign) + Agent 72 Orleans/Client honesty | Keep |

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Client is programming model only | **PASS** | Public exports: `IDigitalBrain`, `DigitalBrainClient` only (`ClientApiContracts`). Methods: ambient-owner `Get` / `SendAsync` / `EmitAsync` + Never-browsable `Connect` + `Owner`. **No** journal Read/Watch/Unwatch on product facade. |
| Metapackage never pulls Kernel/modules | **PASS** | `DigitalBrain.csproj`: ProjectReference Abstractions + Client + Aspire only; `IncludeBuildOutput=false`. Residual pin `MetapackageGraphIsConsumerSurfaceOnly` + packages.md. |
| Abstractions leaf among DigitalBrain packages | **PASS** | `TheAbstractionsPackageIsALeaf`; csproj: `Microsoft.Orleans.Sdk` only among PackageReference; no other `DigitalBrain.*` projects. Identity/synapse/capability vocabulary + session/registry fabric contracts. |
| Orleans grain bases intentional (not domain leak) | **PASS** (Hold #12) | `INeuron : IGrainWithStringKey`, `IJournalObserver : IGrainObserver`, `ISubscriptionRegistry : IGrainWithStringKey` — substrate required for grain wire. packages.md now names `Microsoft.Orleans.Sdk` (was dishonest `nothing`). Domain contracts must not add further grain surfaces. |
| Product journal observation Designed | **PASS** (Hold #7 protect) | `IDigitalBrain` has no watch API. Ui SSE: host-private `ISessionNeuron.ReadNeuronJournal` via `IGrainFactory` (`ShellEventFeed`). `ISessionNeuron` journal methods remain Abstractions wire for edge/Testing — **not** product client facade. packages.md boundary rule now states Designed not Built. |
| Residual holds honest | **PASS** | #7 still Designed; #11 soft CLOSED on WIP (`EditorBrowsable`); #12 reaffirmed intentional; root gate **not claimed** |

### Package role map (re-proof)

| Package | Public product surface | Must stay out |
| --- | --- | --- |
| `DigitalBrain.Abstractions` | Neuron/synapse identity, capability facts, `INeuron`/`ISessionNeuron`/`IHandle`/`IEmit`, journal DTOs, fabric `ISubscriptionRegistry` (Never) | Domain vocabulary (Gmail/CRM/UI/LLM), provider SDKs, Kernel types |
| `DigitalBrain.Client` | `IDigitalBrain` + `DigitalBrainClient` | Kernel, modules, journal observation API, auth boundary claims |
| `DigitalBrain` (metapackage) | No assembly — transitively Abstractions+Client+Aspire | Kernel, Security, Mcp, Testing, any module |

### Scoped verify (Agent 72)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter
  FullyQualifiedName~ResidualPackageGraphContracts
  |FullyQualifiedName~ClientApiContracts
  |FullyQualifiedName~TheAbstractionsPackageIsALeaf

Passed!  Failed: 0, Passed: 14, Skipped: 0, Total: 14
(re-run after foreign session-gate WIP; earlier pass was 11 before peer added pins)

npm --prefix docs test
tests 22 · pass 22 · fail 0
```

**Not root slnx gate.** Residual Client + metapackage + Client API surface + Abstractions leaf + docs site pins only.

### Assess template (band)

```
Scope: src/DigitalBrain.Client/**, src/DigitalBrain.Abstractions/**, src/DigitalBrain/**
What it does: owner-scoped programming model + leaf substrate vocabulary + consumer metapackage.
Consumer today: compositions, Ui/Mcp hosts, Testing, Aspire client DI, module contracts (Abstractions only).
Architecture home: §3 namespaces; §5 client API; packages.md consumer boundary.
Layer: vocabulary (Abstractions) | programming model (Client) | packaging (meta)
Public surface: see role map
Implementation hidden? Client Y (session grain private); Abstractions is the vocabulary leaf
Belongs here? Y
Aligns with modules=neurons+synapses? Client is not a module; Abstractions is substrate for module contracts — Y
Dual path / god helper? No second client facade; Connect is Never + Testing/host only
Delete candidates: none without breaking programming model
Move candidates: none; ISubscriptionRegistry stays Abstractions for grain wire (Never hide)
Verify: residual+Client API 14/14 scoped; docs npm 22/22
Grill 13: see below
```

### Holds after Client/Abstractions/meta grill

| # | Hold | Status after 65–72 | Residual recommendation |
| --- | --- | --- | --- |
| 7 | Product journal observation on `IDigitalBrain` | **Still Designed — protected** | Edge/host-private poll only; packages.md honesty; promote only with non-UI consumer red proof |
| 11 | `ISubscriptionRegistry` public | **Soft CLOSED on WIP** | Peer Never-browsable; keep grain contract; G7 green-claim; do not re-public IntelliSense without author need |
| 12 | Orleans grain bases on Abstractions | **Reaffirmed intentional** | Documented in packages.md; reject domain grain creep |

### What G2 Client band does *not* claim

- Root `dotnet build|test` green (slnx)
- Aspire + Aspire.Hosting ownership (agents 73–80)
- Product journal observation Built
- That Agent 72 authored `ISubscriptionRegistry` Never or `DigitalBrainClient` session-gate — **foreign concurrent peers**

**Docs npm (Agent 72, packages.md honesty only):** `npm --prefix docs test` → **22/22 pass** (not a substitute for G7 root gate).

### Grill board (§2) — Agent 72 condensed

1. **What does it do?** Programming model (`IDigitalBrain`) + leaf substrate + metapackage.  
2. **Consumers today?** Hosts, compositions, Testing, Aspire client DI, all Contracts.  
3. **Architecture place?** packages.md + architecture client API — matches disk after honesty edit.  
4. **Kind?** vocabulary + client facade + packaging.  
5. **Public that should be internal?** Registry soft-hidden on WIP; grain bases must stay public for wire.  
6. **Delete impact?** Breaks entire author model.  
7. **Contracts leak SDK?** Orleans substrate only — deliberate, documented.  
8. **Kernel domain word?** No — Client/Abstractions domain-neutral.  
9. **Invent Behavior / IReminder / Auto?** No.  
10. **Claimed without command?** Scoped 14 tests + docs 22/22 quoted; root slnx unclaimed.  
11. **Foreign dirty?** Registry Never + Client session-gate + AI/Flutter/Kernel/test WIP — surfaced, not reversed.  
12. **Layer move?** No.  
13. **New engineer via architecture alone?** Yes after packages.md Orleans + no-watch honesty.

### Verdict

Client + Abstractions + metapackage **ownership aligns** with architecture and residual package pins. Success = assessed + docs honesty (Orleans deps + Designed journal watch) + residual holds honest — **not** inventing client timeline API or pulling Kernel into consumers. Agent 72 wrote packages.md honesty + scorecard; root gate unclaimed.

*End Wave G2 Client/Abstractions/metapackage (agents 65–72). Agent 72 wrote packages.md + scorecard. Root gate not claimed.*

---

## Wave G2 Aspire.Hosting — Agent 70 (own-audit) — **G2-clean mid-band, not Aspire family exit**

**Mission:** `own-audit` — `AddDigitalBrain` single product brain; silo vs client projection not dual product; fix husks; soft residual owner env dual const.  
**Write scope:** `src/DigitalBrain.Aspire.Hosting/**` (+ this scorecard).  
**Not this agent:** full Aspire + Aspire.Hosting family exit (band 73–80); DigitalBrain.Aspire client DI fold; Flutter/AppHost product sentences; live Healthy.

**Vision restatement:** One AppHost call materializes one durable brain; silo and client are projections of that brain (security split), not two products.

**Codegraph first:** `AddDigitalBrain` → storage/clustering/reminders/journal/Orleans + `DigitalBrainBuilder`. Public surface = `DigitalBrainHostingExtensions` (consts + `AddDigitalBrain`/`AddModule`/`WithReference`×2), `DigitalBrainBuilder` (public type; guts internal + `AsClient()`), `ClientDigitalBrainReference` (typed client token), `DigitalBrainModuleBuilder<T>` (fluent module config). `DigitalBrainModuleProjection` already **internal**. IVT friends: AI/Flutter/Mcp module hosting. Consumers: product AppHost, companion AppHosts, L0 HostingProjectionContracts, module `With*` packages.

### Product sentence residual table

| Candidate dual / husk | Verdict | Why |
| --- | --- | --- |
| `AddDigitalBrain(name)` vs resurrected `AddBrain` / storage-profile / `WithAzureStorage` | **No dual** | Must-not-return already gone; single call owns complete Azure Storage profile (Azurite run / Azure publish) |
| `WithReference(brain)` vs `WithReference(brain.AsClient())` | **Projection split, not dual product** | Architecture §3/§7 + L0 `HostingProjectionContracts`: silo gets journal, state-protection, modules, waits, module projections; client gets Orleans clustering discovery only |
| Multiple brains sharing one cluster / incomplete profile | **Protected by design** | One brain = one homogeneous cluster; incomplete hand-wire fails at journal storage |
| `ClientDigitalBrainReference` as second product type | **Keep** | Typed security token for overload resolution; members internal; not a second hosting path |
| `SetJournal` + nullable `Journal` incomplete-builder path | **Husk deleted (Agent 70)** | Journal is ctor-required non-null; always projected on silo `WithReference` |
| Public `DigitalBrainBuilder.Name` | **Hide-implementation (Agent 70)** | Only IVT module hosting reads name; demoted `internal` |
| Owner ambient dual Flutter hosting ↔ Aspire client | **Layer dual const — soft hold** | Env `DigitalBrain__Owner` ↔ config `DigitalBrain:Owner`; both `"dev"`. Fold blocked: `DigitalBrain.Aspire` must not reach `Aspire.Hosting` (L0 pin `TheAspireClientIntegrationDoesNotReachHosting`); Abstractions must not learn hosting env keys; inventing shared const package = trash. Leave dual; do not invent third home |
| `StateProtectionKeyConfigurationKey` vs Security private `ConfigurationKey` string | **Layer value-match — hold** | Public projection key on hosting; consumer private on Security — not a dual product door |
| `ModulesConfigurationKey` vs Kernel hardcoded `"DigitalBrain:Modules"` | **Layer value-match — hold** | Kernel must not reference Aspire.Hosting; pin remains L0 via hosting public const + Kernel section read |

### Public surface after Agent 70

| Type / member | Visibility | Role |
| --- | --- | --- |
| `DigitalBrainHostingExtensions` | public | Product composition API + config key consts |
| `AddDigitalBrain` / `AddModule` / `WithReference(brain\|AsClient)` | public | Single brain + silo/client projection |
| `DigitalBrainBuilder` | public type | Composition handle; **Name / Journal / guts internal** |
| `AsClient()` | public | Client projection token |
| `ClientDigitalBrainReference` | public type, members internal | Client overload token |
| `DigitalBrainModuleBuilder<T>` | public type, members internal | Module `With*` configure target |
| `DigitalBrainModuleProjection` | internal | Module resource projection guts |

### Product edits (Agent 70 only)

| Path | Change |
| --- | --- |
| `DigitalBrainBuilder.cs` | `Name` → `internal`; journal required in ctor; delete `SetJournal` + nullable field |
| `DigitalBrainHostingExtensions.cs` | pass journal into ctor; always `WithReference(journal)` on silo path |

### Soft residuals (honest, not dual product paths)

| Residual | Hold | Attack |
| --- | --- | --- |
| Owner ambient dual Flutter.Aspire.Hosting ↔ DigitalBrain.Aspire | soft hold | Do not fold without graph-safe home; agents 73–80 / G3 may re-evaluate only if a non-hosting leaf appears for process owner (unlikely — leave) |
| Config key string couples Kernel/Security ↔ Hosting public consts | layer split | Keep hosting as public projection names; do not force Kernel→Hosting edge |
| Live product topology Healthy | Hold #6 | G3 / G7 live-aspire |
| Full Aspire family exit (client DI + hosting together) | band 73–80 | Peer own-audit of `DigitalBrain.Aspire` |

### Scoped verify (Agent 70)

```
dotnet build src/DigitalBrain.Aspire.Hosting -c Release → 0 warnings / 0 errors
dotnet build modules/.../AI.Aspire.Hosting -c Release → 0/0
dotnet build modules/.../Flutter.Aspire.Hosting -c Release → 0/0
dotnet build hosts/DigitalBrain.AppHost -c Release → 0/0
dotnet test tests/DigitalBrain.Tests --filter "FullyQualifiedName~Hosting" -c Release
→ Passed: 27, Failed: 0
```

Root slnx / docs npm / live Aspire **not claimed**.

### Grill board (§2) — Agent 70

1. **What does it do?** One-call durable AppHost brain (storage/Orleans/modules) with silo vs client projection.  
2. **Consumers today?** Product + companion AppHosts; module Aspire.Hosting `With*`; L0 HostingProjectionContracts.  
3. **Architecture place?** §3 Selection / §7 Hosting — `AddDigitalBrain(name)` one complete durable profile; `AsClient()` security boundary.  
4. **Kind?** Infrastructure composition (not vocabulary).  
5. **Public that should be internal?** Projection guts already internal; Agent 70 hid `Name` + journal mutation husk.  
6. **Delete impact?** Breaks all AppHost composition + module hosting projection.  
7. **Contracts leak SDK?** N/A (not Contracts); free of Kernel in public API (L0 pin).  
8. **Kernel domain word?** No.  
9. **Invent Behavior / calendar Time / Auto / IFlutter?** No.  
10. **Claimed without command?** Scoped build + 27 Hosting tests quoted; root unclaimed.  
11. **Foreign dirty?** Client/Abstractions G2 exit, AI/Flutter/Kernel/Integrations/Security/test WIP — surfaced, not reversed.  
12. **Layer move?** Owner dual cannot move into this package without forcing client→Hosting edge (forbidden).  
13. **New engineer via architecture alone?** Yes — packages.md + §3/§7 match disk.

### Verdict

`DigitalBrain.Aspire.Hosting` **ownership aligns**: single product brain, silo/client are **projections not dual products**, husks (`SetJournal`/nullable journal incomplete path, public `Name`) folded. Owner env dual remains **soft hold** (package-graph honest). Success = mid-band assessed + small hide-implementation edit + residuals listed — **not** full Aspire family exit. Agent 70 wrote product C# in write scope + scorecard; root gate unclaimed.

*End Agent 70 Aspire.Hosting own-audit mid-band. Root gate not claimed. Aspire family exit remains agents 73–80.*

---

## Wave G2 — Agent 71 (Aspire package graph residual + boundary) — **G2-clean mid-band residual pin, not Aspire family exit**

**Mission:** `own-audit` — residual package graph + boundary honesty for `DigitalBrain.Aspire` and `DigitalBrain.Aspire.Hosting`.  
**Write scope:** `tests/DigitalBrain.Tests/Packages/PackageInventory.cs`, `tests/DigitalBrain.Tests/Packages/ResidualPackageGraphContracts.cs`, this scorecard.  
**Not this agent:** product C# under `src/DigitalBrain.Aspire*` (Agent 70 hosting mid-band; family exit 73–80); Client/Abstractions/meta exit (65–72 already COMPLETE); live Healthy.

**Vision restatement:** Consumer Aspire DI and AppHost Aspire.Hosting are separate package graphs — client surface never pulls hosting; hosting never pulls Client/Kernel/modules.

**Codegraph first:** `DigitalBrain.Aspire` → `Client` + `Microsoft.Orleans.Client` → `AddDigitalBrainClient` / `IDigitalBrain`. `DigitalBrain.Aspire.Hosting` → `Abstractions` + Aspire host packages → `AddDigitalBrain` / `DigitalBrainBuilder`. Residual pins previously covered Client/Security/Mcp/metapackage/Testing only — **gap** was Aspire family exact residual.

### Ownership assessment

| Package | Direct ProjectReference | Direct PackageReference (flows) | Compile-reachable | Forbidden residual |
| --- | --- | --- | --- | --- |
| `DigitalBrain.Aspire` | `Client` | `Microsoft.Orleans.Client` | `Abstractions`, `Client` | Kernel, Security, Testing, **Aspire.Hosting**, Integrations, modules |
| `DigitalBrain.Aspire.Hosting` | `Abstractions` | `Aspire.Hosting`, `Aspire.Hosting.Azure.Storage`, `Aspire.Hosting.Orleans` | `Abstractions` | Kernel, Client, Aspire (client DI), Security, Testing, Integrations, modules, Ui family |
| Metapackage (pre-existing pin) | Abstractions, Client, Aspire | (none) | same | consumer residual now also bans **Aspire.Hosting** |

**packages.md match:** Aspire → Client; Aspire.Hosting → Abstractions. Residual pins package NuGets more precisely than packages.md project-level Depends-on (same honesty pattern as Client → Orleans.Client).

**Boundary already green (no product edit):**

- `ConsumerPath` includes Aspire + Aspire.Hosting — no provider SDK / MAF / Testing / Dart-Flutter SDK
- `TheAspireClientIntegrationDoesNotReachHosting` assembly reach
- `HostingPublicApiIsFreeOfKernelTypes` over all HostingPackages
- Agent 70 product sentence: single `AddDigitalBrain`; silo/client projections not dual products

### Product C#

**None.** Graph already correct on disk; residual was the missing witness. Concurrent Agent 70 product edits under `Aspire.Hosting` do **not** change csproj graph — residual still green after those WIP changes.

### Soft residuals left for 73–80 / peers

| Residual | Status | Recommendation |
| --- | --- | --- |
| Owner ambient dual Flutter.Aspire.Hosting ↔ DigitalBrain.Aspire | soft hold (Agent 70) | Do not fold across package-graph ban |
| Config key string couples Kernel/Security ↔ Hosting consts | layer split | Keep hosting as public projection names |
| Full Aspire public-surface grill (client DI exports, hide-implementation depth) | open | Agents **73–80** family exit |
| Live product topology Healthy | Hold #6 | G3/G7 — not residual graph |

### Verify (scoped — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~ResidualPackageGraphContracts"
→ Passed: 7, Failed: 0  (was 5; +AspireGraphIsClientSurfaceOnly +AspireHostingGraphIsAbstractionsAndHostPackagesOnly)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~AssemblyBoundaryContracts|FullyQualifiedName~ContractsPackageBoundaryContracts|FullyQualifiedName~HostingPackageBoundaryContracts"
→ Passed: 71, Failed: 0
```

Root slnx / docs npm / live Aspire **not claimed**.

### Grill board (§2) — Agent 71

1. **What does it do?** Pins exact residual package graphs for consumer Aspire DI and AppHost Aspire.Hosting.  
2. **Consumers today?** Metapackage + Ui/Mcp hosts (Aspire); all AppHosts + module `With*` (Aspire.Hosting).  
3. **Architecture place?** packages.md consumer vs silo hosting split; §3/§7.  
4. **Kind?** Boundary witness (tests), not product surface.  
5. **Public that should be internal?** N/A this cycle — Agent 70 already hid hosting guts mid-band.  
6. **Delete impact?** Losing residual lets consumer path re-acquire Aspire.Hosting or Kernel without fail-mode.  
7. **Contracts leak SDK?** No.  
8. **Kernel domain word?** Residual forbids Kernel on both Aspire packages.  
9. **Invent Behavior / Auto / IFlutter?** No.  
10. **Claimed without command?** Scoped Residual **7/7** + boundary **71/71** quoted; root unclaimed.  
11. **Foreign dirty?** Agent 70 Aspire.Hosting product C#; Agent 72 Client exit; AI/Flutter/Integrations/test WIP — surfaced, not reversed.  
12. **Layer move?** No product move; residual only.  
13. **New engineer via architecture alone?** packages.md + residual pins now match disk for Aspire family graphs.

### Verdict

Aspire package graphs **ownership-aligned and residual-pinned**. Consumer Aspire never reaches Aspire.Hosting/Kernel/modules; Aspire.Hosting stays Abstractions + host packages only. Success = residual gap closed + boundary re-proof — **not** Aspire family exit (73–80) and **not** product C# authorship. Agent 71 wrote residual inventory + facts + scorecard; root gate unclaimed.

*End Agent 71 Aspire residual package graph + boundary. Root gate not claimed. Aspire family exit closed by Agent 80.*

---

## Wave G2 Aspire + Aspire.Hosting exit (agents 69–71, 80) — **COMPLETE with honest residuals**

**Mission (Agent 80):** `docs-honesty`  
**Exit criteria (prompt §7 G2 cross-cutting / residual map):** one-call durable AppHost composition honesty · consumer Aspire DI never reaches Aspire.Hosting · silo vs `AsClient` projection security split · no dual product brain · packages.md graph honesty · Built composition ≠ Built-live product topology.

**Numbering note:** prompt §7 table lists Aspire + Aspire.Hosting as agents **73–80**. Orchestrator compressed mid-band to **69–71** (69 client DI own-audit re-proof; 70 hosting sentence/husk; 71 residual graph) and assigned **80** as docs-honesty closer. Scorecard uses the orchestrator band.

Quoted at finalize (Agent 80, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 80 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- src/DigitalBrain.Aspire src/DigitalBrain.Aspire.Hosting docs/packages.md
 M docs/packages.md
 M src/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs
 M src/DigitalBrain.Aspire.Hosting/DigitalBrainHostingExtensions.cs
```

**Foreign dirty (Agent 80 did not author product C#):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `src/DigitalBrain.Aspire.Hosting/*` | Agent 70: journal ctor-required; delete `SetJournal`; `Name`→internal; always silo journal `WithReference` | Mid-band product — **do not reverse** |
| AI / Flutter hosting / Kernel / Client / Integrations / Security / test WIP | Concurrent campaign bands | Surface only; leave unstaged |
| `docs/packages.md` | Prior Client/MEAI honesty + **Agent 80 Aspire NuGet + consumer/AppHost split** | Keep |
| This scorecard | Agent 16…80 campaign record | Campaign record |

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Single product brain (`AddDigitalBrain`) | **PASS** | One public factory on `DigitalBrainHostingExtensions`; owns Azure Storage + clustering/reminders/journal + Orleans service; must-not-return `AddBrain` / storage-profile duals absent. Agent 70: journal always present (no incomplete nullable path). |
| Silo vs `AsClient` is projection security split, not dual product | **PASS** | `WithReference(brain)` → Orleans + journal + waits + modules + projections + optional state-protection; `WithReference(brain.AsClient())` → `Orleans.AsClient()` clustering discovery only. Architecture §3/§7 + L0 `HostingProjectionContracts`. |
| Consumer Aspire never reaches Aspire.Hosting | **PASS** | `DigitalBrain.Aspire` → Client + `Microsoft.Orleans.Client` only. Residual `AspireGraphIsClientSurfaceOnly`; assembly pin `TheAspireClientIntegrationDoesNotReachHosting`. packages.md now names the ban. |
| Aspire.Hosting graph free of Client/Kernel/modules | **PASS** | Direct: Abstractions + `Aspire.Hosting` / `Azure.Storage` / `Orleans`. Residual `AspireHostingGraphIsAbstractionsAndHostPackagesOnly`. `HostingPublicApiIsFreeOfKernelTypes`. |
| Client DI surface is programming-model wiring only | **PASS** (Agent 69 re-proof) | Public exports: `DigitalBrainClientHostingExtensions` only — `DefaultOwner`, `OwnerConfigurationKey`, `ResolveOwner`, `AddDigitalBrainClient`×2. Registers `IDigitalBrain` via `DigitalBrainClient.Connect` (Never-browsable). **No** journal watch API; **no** Kernel/modules. |
| Hosting public surface hides projection guts | **PASS** (Agent 70) | Public: hosting extensions + consts, `DigitalBrainBuilder` (guts/`Name`/`Journal` **internal**), `AsClient()`, `ClientDigitalBrainReference` (members internal), `DigitalBrainModuleBuilder<T>` (members internal). `DigitalBrainModuleProjection` **internal**. IVT: AI/Flutter/Mcp module hosting only. |
| Docs honesty Built vs residual | **PASS** | packages.md + architecture §3/§7 match disk for one-call durable profile + silo/`AsClient` split. Agent 80 table lists Orleans.Client + Aspire host NuGets (was project-level only). L0/L2 composition **Built**; product AppHost OS Healthy **not** Built-live (Hold #6). |
| Residual holds honest | **PASS** | Owner ambient dual soft hold; config-key couples layer split; root gate **not claimed** |

### Package role map (re-proof)

| Package | Direct ProjectReference | Direct PackageReference | Public product surface | Must stay out |
| --- | --- | --- | --- | --- |
| `DigitalBrain.Aspire` | Client | `Microsoft.Orleans.Client` | `DigitalBrainClientHostingExtensions` (`AddDigitalBrainClient` + owner) | Kernel, modules, Aspire.Hosting, Integrations, Testing, journal observation |
| `DigitalBrain.Aspire.Hosting` | Abstractions | `Aspire.Hosting`, `Aspire.Hosting.Azure.Storage`, `Aspire.Hosting.Orleans` | `AddDigitalBrain` / `AddModule` / `WithReference`×2 / builder + client token types | Client, Kernel, modules, Integrations, Ui family, consumer Aspire |

**Line-count (product Aspire `*.cs`, excl bin/obj):** max **113** `DigitalBrainBuilder.cs` — under 400.

### Soft residuals (not dual product paths)

| Residual | Status after 69–71, 80 | Residual recommendation |
| --- | --- | --- |
| Owner ambient dual Flutter.Aspire.Hosting (`DigitalBrain__Owner` / `"dev"`) ↔ Aspire client (`DigitalBrain:Owner` / `"dev"`) | **soft hold** | Graph-safe fold blocked: consumer Aspire must not reference Aspire.Hosting; Abstractions must not learn hosting env keys; inventing shared const package = trash. Leave dual. |
| Config key string couples Kernel/Security ↔ Hosting public consts | **layer split** | Hosting remains public projection names; do not force Kernel→Hosting edge |
| Live product topology Healthy | **Hold #6** | G3 Ui / G7 live-aspire — not Aspire package ownership |
| Root slnx build/test | **unquoted** | G7 only |

### Docs honesty re-proof (Agent 80 mission)

| Source | Built claim | Designed / residual | Verdict |
| --- | --- | --- | --- |
| `docs/packages.md` Aspire rows | Client DI vs one-call AppHost composition | NuGets + never-cross-package bans now explicit | **Honest** (Agent 80) |
| `docs/packages.md` narrative | complete durable profile; silo vs client projection | OS Healthy not claimed via hosting package | **Honest** |
| `docs/architecture.md` §3/§7 | `AddDigitalBrain(name)` one profile; `AsClient` security | incomplete hand-wire fails; no second durability-provider abstraction | **Honest** (matches Agent 70 disk) |
| Consumer residual | Aspire on consumer path | never Aspire.Hosting | **Honest** (Agent 71 pins) |
| Product AppHost | composition via hosting package | not Built-live OS topology | **Hold #6** |

### What G2 Aspire band does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- Product AppHost `aspire start` / `aspire run` Healthy for OS surface (Hold #6)
- That owner dual was collapsed (soft hold — graph-correct)
- That Agent 80 authored Aspire product C# — **scorecard + packages.md only** (Agent 70 owns hosting C# husks)
- Security / Integrations.Mcp / Testing library family exits (81–96)

### Peer summary (agents 69–71 → 80)

| Agent | Focus | Folded result |
| --- | --- | --- |
| 69 | Client DI surface own-audit | **G2-clean on re-proof:** single public extensions type; owner config only; no Hosting reach |
| 70 | Hosting product sentence + husks | **G2-clean mid-band:** `SetJournal` deleted; `Name` internal; journal always on silo path |
| 71 | Residual package graph + boundary | **G2-clean:** exact Aspire + Aspire.Hosting residual pins; **7/7** residual family at mid-band |
| 80 | Docs-honesty exit | This block; packages.md NuGet + split honesty; root unclaimed |

### Scoped verify (Agent 80 — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ResidualPackageGraphContracts"
→ Passed: 9, Failed: 0

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ResidualPackageGraphContracts
  |FullyQualifiedName~TheAspireClientIntegrationDoesNotReachHosting
  |FullyQualifiedName~HostingPublicApiIsFreeOfKernelTypes"
→ Passed: 11, Failed: 0  (includes residual + 2 boundary witnesses)

dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ResidualPackageGraphContracts
  |FullyQualifiedName~TheAspireClientIntegrationDoesNotReachHosting
  |FullyQualifiedName~HostingPackageBoundaryContracts
  |FullyQualifiedName~HostingProjectionContracts
  |FullyQualifiedName~FlutterHosting"
→ Passed: 24, Failed: 0

npm --prefix docs test
→ tests 22 · pass 22 · fail 0
```

Root slnx **not claimed**. Live Aspire **not claimed**.

### Grill board (§2) — Agent 80 condensed

1. **What does it do?** Consumer Generic Host DI + AppHost one-call durable brain with silo/client projections.  
2. **Consumers today?** Ui/Mcp hosts (Aspire); all AppHosts + module `With*` (Aspire.Hosting); metapackage pulls consumer Aspire only.  
3. **Architecture place?** §3 Selection / §7 Hosting + packages.md — match disk after honesty edit.  
4. **Kind?** Infrastructure composition + client host wiring (not vocabulary).  
5. **Public that should be internal?** Projection guts already internal (Agent 70). Public builder/`AsClient` tokens are deliberate composition API.  
6. **Delete impact?** Breaks all AppHost composition and host client DI.  
7. **Contracts leak SDK?** N/A; hosting free of Kernel public types; consumer free of hosting/modules.  
8. **Kernel domain word?** No.  
9. **Invent Behavior / Auto / IFlutter / second storage profile abstraction?** No.  
10. **Claimed without command?** Scoped residual/boundary + Hosting projection band + docs 22/22 quoted; root unclaimed.  
11. **Foreign dirty?** Agent 70 product C#; concurrent AI/Flutter/Client/test WIP — surfaced, not reversed.  
12. **Layer move?** Owner dual cannot fold without forbidden graph edge.  
13. **New engineer via architecture alone?** Yes — packages.md + §3/§7 match disk for Aspire family.

### Verdict

Aspire + Aspire.Hosting **ownership aligns** with architecture §3/§7 and residual package pins. Success = assessed + docs honesty (NuGet deps + consumer vs AppHost split + Built-not-live) + mid-band husks retained + soft residuals honest — **not** inventing a shared owner package, **not** claiming product AppHost Healthy Built-live. Agent 80 wrote packages.md honesty + scorecard; root gate unclaimed.

*End Wave G2 Aspire + Aspire.Hosting (agents 69–71, 80). Agent 80 wrote packages.md + scorecard. Root gate not claimed.*

---
## Wave G1 Flutter mid-band progress (agents 53-59; Agent 60 docs-honesty) - **MID-BAND COMPLETE, not family exit**

**Mission (Agent 60):** `docs-honesty`  
**Scope:** lock Flutter mid-band progress for agents **53-59** from peer journals (54, 59; 57 return) + on-disk re-proof + scoped verify. Concurrent peers **62** (hosting dual sentence) and **63** (test-contract) continued past this mid-band lock - folded as progress, not claimed as this agent's authorship.  
**Exit criteria (prompt section 7 G1 - mid-band answers only):** ships neurons/synapses? hides SDK/impl? hosting optional package correct?  
Flutter-specific: **first-five?** · **neurons internal / no Dart on C# contracts?** · **WithUiEdge/WithFlutterHost Desktop|Headless no Auto?** · **clients dual-golden edge purity?** · residuals: **not Built-live (Hold #6)** · **Designed chrome / journal observation** · **no IFlutter**.

**Numbering note:** prompt table lists Salesforce **49-56** and Flutter **57-64**. Orchestrator closed Salesforce as **46-52** and ran Flutter mid-band as **53-59** with Agent **60** = docs-honesty mid-band lock. Concurrent agents **62-63** also journaled mid notes. **This is not G1 Flutter family COMPLETE.**

Quoted at finalize (Agent 60, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 60 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- modules/DigitalBrain.Modules.Flutter modules/DigitalBrain.Modules.Flutter.Contracts
(empty - Flutter Contracts + runtime clean)

git status --porcelain -- modules/DigitalBrain.Modules.Flutter.Aspire.Hosting
 M FlutterHostLaunch.cs
 M FlutterHostingExtensions.cs
(foreign concurrent encapsulate - not Agent 60)
```

**Foreign dirty (Agent 60 did not author product C#):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `Flutter.Aspire.Hosting/FlutterHostLaunch.cs` | ShellPackageDirectoryName private; Result/Resolve internal; ResolveDesktopPackageDirectory private | Concurrent G1 Flutter peer encapsulate - **do not reverse** |
| `Flutter.Aspire.Hosting/FlutterHostingExtensions.cs` | Drop narrative host-kind comments | Same peer - keep |
| `tests/.../Flutter/*`, `Flutter.Tests/*`, wire golden dart | Concurrent L0/L1 honesty WIP (Agents 58/63 band) | **Do not reverse** |
| AI / Kernel / packages.md / Time / Tasks / Integrations | Other G1 bands | **Do not reverse** |
| This scorecard | Multi-agent campaign record + Agent 60 mid-band | Campaign record |

### Mid-band exit answers (not full family close)

| Criterion | Result | Evidence |
| --- | --- | --- |
| Ships neurons / synapses | **PASS** | **Contracts** public first-five only: `IShell` (Open(OpenScene) + ClientEntryPoint), `IScene`, `OpenScene`, `SceneOpened`, `ControlActivated` (: Synapse). Namespace DigitalBrain.Flutter. Wire golden pins same five (Agent 57: single oracle C# + Dart). **No** IFlutter. Contracts csproj: **Abstractions-only**; zero PackageReference. |
| Hides SDK / implementation | **PASS** | **Runtime** PE export = **FlutterModule only** (Agent 54). ShellNeuron/SceneNeuron **internal sealed**. Runtime csproj: Contracts + Kernel only. Hosting launch helpers **internal** (foreign WIP narrows further). Clients pure-Dart HTTP of Ui, never Orleans (Agent 57). |
| Hosting optional package correct | **PASS** | Separate packable Aspire.Hosting: WithUiEdge / WithFlutterHost() Desktop default / WithFlutterHost of DesktopHost or HeadlessHost. **No Auto.** Missing markers throw. Exclusive env DIGITALBRAIN_UI_BASE + DIGITALBRAIN_SHELL. Product AppHost single WithUiEdge().WithFlutterHost() (Agent 59); no direct Ui ProjectReference. Fixtures vocabulary-only omit hosting. Agent 62: hosting dual product sentence **G1-clean**. FlutterHosting selection/projection contracts **10/10**. |
| Residual holds (not fake green) | **PASS** | Hold **#6** reaffirmed; Designed chrome/journal/IdP protected; soft residuals listed; root gate **not claimed**; family exit **not** claimed |

### Package role map (re-proof)

| Package | Direct ProjectReference (compile) | Public product surface | Must stay out |
| --- | --- | --- | --- |
| DigitalBrain.Modules.Flutter.Contracts | Abstractions | First-five + wire golden | Dart/Flutter SDK, Widget types, HTTP edge, Kernel, Ui host |
| DigitalBrain.Modules.Flutter | Contracts, Kernel | FlutterModule only; neurons **internal** | Public neurons; Dart; Ui project; MCP-as-UI-bus |
| DigitalBrain.Modules.Flutter.Aspire.Hosting | Flutter runtime, Aspire.Hosting | WithUiEdge / WithFlutterHost* + Desktop/Headless markers + options | Auto; journal/Orleans env on Flutter |
| clients/digitalbrain_wire + digitalbrain_flutter (+ shell/) | (Dart) | Edge DTOs + HTTP/SSE + Headless/Desktop hosts | Orleans, MCP-as-UI, C# Kernel |

**deps.json (Release):** Contracts -> Abstractions; Runtime -> Kernel + Flutter.Contracts; Aspire.Hosting -> Aspire.Hosting + Flutter. **packages.md match:** yes.  
**Line-count:** max ~229 FlutterHostingExtensions.cs - under 400.

### Peer fold map (53-59 -> 60)

| Agent | Focus | Result | Source |
| --- | --- | --- | --- |
| 53 | Contracts first-five | **G1-clean** Abstractions-only; dual golden; no IFlutter | On-disk re-proof |
| 54 | Runtime encapsulate | **G1-clean** PE only FlutterModule; neurons internal | Scorecard block |
| 55 | Package graph + hosting optional | **G1-clean** three-pack = packages.md | On-disk re-proof |
| 56 | Hosting Desktop/Headless honesty | **G1-clean** no Auto; throws; exclusive env | On-disk + FlutterHosting 10/10 |
| 57 | Clients + dual golden | **G1-clean** pure-Dart root / nested shell; one golden | Peer return (scorecard fold) |
| 58 | L0/L1 pin honesty | Concurrent foreign test WIP; L1 journals 2/2 at re-proof | Foreign dirty honesty |
| 59 | Ui hand-wire vs With* | **G1-clean** single AppHost OS sentence | Scorecard block |
| 60 | Docs-honesty mid-band | This block | Scorecard only |

**Beyond mid-band (not claimed as 53-59 work, noted for honesty):** Agent **62** hosting dual product sentence G1-clean; Agent **63** FlutterContracts boundary pins G1-clean.

### What G1 Flutter mid-band does *not* claim

- Product AppHost OS-surface **Healthy** / live aspire start as Built - **Hold #6**
- Full product chrome beyond key/title; product journal observation; multi-principal IdP - **Designed**
- Root `dotnet build|test DigitalBrain.slnx` / docs npm green - **not run / not claimed**
- Full G1 Flutter **family exit COMPLETE**
- That Agent 60 authored Flutter product, client, Ui, or test C# - **scorecard only**
- Authorship of foreign FlutterHostLaunch visibility narrow or concurrent Flutter test WIP

### Holds after Flutter mid-band grill

| # | Hold | Status after 53-60 | Residual recommendation |
| --- | --- | --- | --- |
| 6 | Flutter not Built-live | **open** | Never promote L0/L1/projection to live; G3 Ui + G7 |
| - | Product journal observation / full chrome / IdP | **Designed - protect** | Do not invent |
| - | IFlutter / Auto / Dart->Orleans | **absent - protect** | Must-not-return |
| - | Optional PE export pin (Time twin) | soft | G5 / exit peer if desired |
| - | Soft UiHealthPath string couple + Dart route-const | soft | optional G3 |
| - | Family exit block | **open** | Remaining Flutter peers / docs-honesty closer |

### Verify (scoped - **not** root gate)

```
dotnet build modules/DigitalBrain.Modules.Flutter.Contracts -c Release
-> Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Flutter -c Release
-> Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build modules/DigitalBrain.Modules.Flutter.Aspire.Hosting -c Release
-> Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Flutter.Tests -c Release
-> Passed: 2, Failed: 0

dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~FlutterHosting"
-> Passed: 10, Failed: 0
```

Agent 57 quoted Dart/Flutter client gates separately. Root slnx / docs npm / live Aspire **not claimed**.

### Grill board (section 2) - Agent 60 condensed

1. **What does it do?** OS-surface vocabulary + shell/scene neurons + optional Ui/Flutter host projection + dual-golden edge clients.
2. **Consumers today?** Product AppHost; fixtures; HostingProjection/FlutterHosting contracts; wire golden; clients.
3. **Architecture place?** section 4.6 Built code/L0/L1 + projection; **not** Built-live - Hold #6.
4. **Kind?** Module vocabulary + runtime + optional OS hosting + edge clients.
5. **Public that should be internal?** None material - neurons + launch helpers internal.
6. **Delete impact?** Breaks OS surface composition, L1 journals, dual golden, shell chrome.
7. **Contracts leak SDK?** No.
8. **Kernel domain word?** No.
9. **Invent Behavior / IReminder / Auto / IFlutter?** No.
10. **Claimed without command?** Root gate unclaimed; scoped build/test quoted.
11. **Foreign dirty?** AI/Time/Kernel + Flutter hosting visibility narrow + Flutter test WIP - surfaced, not reversed.
12. **Layer move?** No.
13. **New engineer via architecture alone?** Yes - section 4.6 + packages.md match disk.

### Verdict

Flutter family **ownership aligns** with architecture section 4.6 and packages.md for Built first-vertical vocabulary + L0/L1 + module hosting projection + edge clients. Success = mid-band assessed + Hold #6 / Designed residuals honest - **not** inventing Built-live Healthy. Agent 60 wrote scorecard only; root gate unclaimed. **Subsequent Agent 64 closed Flutter family exit COMPLETE** (see family exit block) - this mid-band block remains the 53-59 progress lock, not a reversal of that exit.

*End Wave G1 Flutter mid-band (agents 53-59 progress; Agent 60 docs-honesty). Agent 60 wrote scorecard only. Root gate not claimed. Family exit later closed by Agent 64.*

---

## Agent 87 (G2 own-audit) — ProductSurfaceResources vs hosts dual → **G3 only**

**Mission:** Own-audit any remaining G2 dual between product AppHost catalog and host const surfaces. Write only if a G2-safe fold exists; otherwise residual note for G3.

**Decision: residual hold. No product C#. Note for G3 only.**

### Ground (Agent 87)

| Field | Content |
| --- | --- |
| HEAD | `c2c27f2446f1620a22e9c0905cac0dad94aa57c3` (unchanged from baseline) |
| Porcelain | Foreign concurrent WIP present (AI/Flutter/tests/docs); **left unstaged** |
| Root gate | **not run / not claimed** |
| Scope writes | This scorecard section only |

### Dual inventory (re-proof from source + msbuild)

#### A. Load-bearing dual (hold — G3 hosts 113–120)

| Concern | `ProductSurfaceResources` (AppHost-internal) | `McpHost` (hosts/Mcp public) |
| --- | --- | --- |
| Aspire resource name | `Mcp` = `"digitalbrain-mcp"` | `ResourceName` = `"digitalbrain-mcp"` |
| HTTP endpoint name | `McpHttpEndpointName` = `"http"` | `HttpEndpointName` = `"http"` |
| HTTP port | `McpHttpPort` = `5000` | `HttpPort` = `5000` |

**Why AppHost cannot type-pin `McpHost` (msbuild quoted Agent 87):**

`dotnet msbuild hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj -getItem:ProjectReference` on `DigitalBrain.Mcp` / `DigitalBrain.Host`:

| Metadata | Value |
| --- | --- |
| `IsAspireProjectResource` | `true` |
| `ReferenceOutputAssembly` | `false` |
| `ExcludeAssets` | `all` |

Contrast: `Flutter.Aspire.Hosting` / other `*.Aspire.Hosting` use `IsAspireProjectResource=false` — AppHost **can** and **does** bind `FlutterHostingExtensions.OwnerEnvironmentVariable` / `DefaultOwner` in `AppHost.cs`. Ui/Flutter **resource names live on the hosting package**, so they are **not** duplicated on `ProductSurfaceResources`.

**Non-overlap (not dual):** AppHost-only `Brain` / `Silo` / `Website` / `WebsiteContentPath`. Mcp process-only `EndpointPath` / `HealthPath` / `HealthResponse` / tools / `MapMcpHost`.

**Consumers today:**

| Site | Uses |
| --- | --- |
| `hosts/DigitalBrain.AppHost/AppHost.cs` | `ProductSurfaceResources.Mcp` + port/endpoint for `AddProject` / `WithHttpEndpoint` |
| `hosts/DigitalBrain.Mcp/**` | process consts + tools; `ResourceName`/`HttpPort` not required for `MapMcpHost` |
| `tests/**` | **zero** typed refs to `ProductSurfaceResources` or `McpHost` for resource name/port |
| `.mcp.json` / `launchSettings.json` | hardcode `digitalbrain-mcp` / `5000` (out of C# const rail) |

Affirms test-truth Agent **158** hold and architecture Hold **#13**. Collapse options remain wrong without a consumer: shared package invents surface; forcing `ReferenceOutputAssembly=true` fights Aspire SDK and pulls MCP graph into AppHost; `IsAspireProjectResource=false` breaks orchestration.

Long-term optional G3 shape (not forced): MCP `*.Aspire.Hosting` module (Flutter pattern) owning Aspire identity strings so AppHost drops the value-match copy. **Only if a real consumer appears.**

#### B. Soft intentional couples (not ProductSurfaceResources duals — G3 optional honesty)

| Couple | Sites | Verdict |
| --- | --- | --- |
| `"/health"` | `UiEdgeContract.HealthPath`, `McpHost.HealthPath`, silo `Host/Program.cs` local, `FlutterHostingExtensions.UiHealthPath`, `TestingAppHostFixture.HealthPath`, Quickstart host literal | Conventional edge health path — soft couple across process boundaries; **do not invent shared health package** solely to collapse |
| `"silo"` / `"brain"` | `ProductSurfaceResources.Silo`/`Brain` vs `TestingAppHost` locals + `TestingAppHostFixture.SiloResourceName` | **Agent 84 hold:** residual L2 uses TestingAppHost fixture names — **not** product catalog. HostTests must never type-bind `ProductSurfaceResources` |

#### C. Correctly non-dual (no action)

| Surface | Owner | Why clean |
| --- | --- | --- |
| Ui routes / SSE event names | `UiEdgeContract` | Single public edge const; Ui tests bind via `UiFixture` |
| Ui / Flutter Aspire resource names + owner env | `FlutterHostingExtensions` | Hosting package compile-reachable from AppHost (`IsAspireProjectResource=false`); AppHost does **not** hand-wire Ui |
| MCP process protocol | `McpHost` | Edge host owns tools/path/health mapping |
| Product OS catalog (brain/silo/mcp/website) | `ProductSurfaceResources` internal | AppHost-only; not HostTests oracle |

### Grill (Agent 87)

1. **No G2 consumer for collapse?** Yes — zero typed test pins on either side for MCP Aspire identity; collapse only invents package or fights ExcludeAssets.
2. **Claimed without command?** msbuild ProjectReference flags quoted above; source values read from both files; tests grep = zero matches.
3. **Foreign dirty?** Concurrent AI/Flutter/tests/docs WIP present at session start — **not reversed, not staged**.
4. **Hold vs delete?** Dual is load-bearing under Aspire project-resource assets — **hold for G3**; not trash dual.
5. **G2 write scope?** None — dual is hosts/AppHost ownership (prompt band **113–120**, wave **G3**). G2 band 81–88 residual on Integrations.Mcp empty-export pin is a separate soft item (Hold #10).

### Residual handoff → G3 (hosts 97–128)

| # | Item | G3 action |
| --- | --- | --- |
| 1 | `ProductSurfaceResources.Mcp*` × `McpHost` Aspire identity dual | **Hold** under ExcludeAssets unless MCP Aspire.Hosting module is justified by a real consumer |
| 2 | HostTests × product catalog | Keep Agent **84** hold — residual L2 = `TestingAppHostFixture` only |
| 3 | Soft `/health` string couples | Optional honesty only; do not invent shared health const package |
| 4 | `.mcp.json` / launchSettings hardcodes | Out of C# rail; optional docs honesty if product MCP edge is grilled |
| 5 | AppHost single product sentence | Already uses module `With*` for Ui/Flutter + internal catalog for silo/mcp/website — re-prove edge-only purity in G3 host grill |

**Closed this cycle:** no silent new dual invented; Flutter OS names confirmed non-dual (hosting package owns them); Agent 158/84/Hold #13 reaffirmed with fresh msbuild oracle.

*End Agent 87. Scorecard only. No product/test C#. Root gate not claimed. Dual remains Explicit residual for G3.*

---

## Wave G2 — Agent 79 (own-audit) — Security + Integrations residual duals — **G2-clean mid-band, not family exit**

**Mission:** `own-audit` — residual duals inside `DigitalBrain.Security`, `DigitalBrain.Integrations.Mcp`, and `DigitalBrain.Integrations.Mcp.Aspire.Hosting`; scorecard mid notes.  
**Write scope:** this scorecard only (no product/test C#).  
**Not this agent:** Security/Integrations **family exit** (prompt band **81–88**); host `ProductSurfaceResources`×`McpHost` dual (Agent **87** → **G3**); Aspire family exit (73–80); Testing library (89–96); root gate.

**Vision restatement:** Southbound MCP + purpose-bound encryption are shared **mechanics** — modules own vocabulary; northbound MCP host is a different edge; zero dual product doors.

**Codegraph first:** `DurablePayloadProtectionHosting` / `IDurablePayloadProtector` (Security — IVT AI + Integrations.Mcp); `McpRuntimeHosting` / `McpRuntime` / `McpOAuth*` / `DurableMcpTokenCache` / `HttpMcpClientSessionFactory` (Integrations.Mcp — IVT Google/Salesforce/Testing/Integrations.Tests); `McpProviderHosting` (Mcp.Aspire.Hosting — IVT Google/Salesforce Aspire.Hosting). Callers: `AIModule` + `GoogleModule`/`SalesforceModule` configure shared protector/runtime once via `TryAddSingleton`; `WithGmail`/`WithSalesforce` both call single `McpProviderHosting.Register`.

### Assess template (packages)

| Scope | What it does | Consumer today | Architecture home | Layer | Public surface | Impl hidden? | Belongs? | Dual path / god helper? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `DigitalBrain.Security` | Purpose-bound AES-GCM durable payload protect/unprotect + DI `Configure` | AI MAF sessions; MCP OAuth token cache | packages.md shared encryption; arch §4 provider modules hide transport | infrastructure | **0 exported types** | Y — all internal/`file`; key private | **Y** | No second protector product API |
| `DigitalBrain.Integrations.Mcp` | Southbound MCP session, OAuth, durable token cache, tool fingerprint, `McpRuntime.RunAsync` | Google + Salesforce neurons; Testing scripted edge; Integrations.Tests | packages.md southbound shared mechanics; arch §4.3 | infrastructure (transport) | **0 exported types** | Y — all internal; SDK types never leave package | **Y** | No REST/Google.Apis/SF dual; single `McpRuntime` |
| `DigitalBrain.Integrations.Mcp.Aspire.Hosting` | Shared AppHost OAuth parameter + env projection for MCP-backed modules | Google/Salesforce `With*` hosting only (IVT) | packages.md shared AppHost projection | edge projection | **0 exported types** | Y — friend-only `McpProviderHosting` | **Y** | Single `Register` for both providers |

**Aligns with modules=neurons+synapses?** Y for consumers — these packages intentionally ship **no** neurons/synapses; modules own vocabulary.  
**Delete candidates:** none (all three packages have consumers).  
**Move candidates:** none — hosting projection correctly does **not** reference Integrations.Mcp runtime (keeps MCP SDK off AppHost projection graph).

### Residual dual inventory (Agent 79)

| Candidate dual | Sites | Verdict | Why / hold |
| --- | --- | --- | --- |
| Southbound Integrations.Mcp vs northbound `hosts/DigitalBrain.Mcp` | packages.md + csproj graphs | **Not a dual product door** | Deliberate separation: southbound client transport vs northbound neuron MCP **server**. Different NuGets (`ModelContextProtocol.Core` vs `ModelContextProtocol.AspNetCore`). Do not merge packages |
| `ProductSurfaceResources.Mcp*` × `McpHost` Aspire identity | AppHost-internal vs hosts/Mcp public | **Out of family — G3 Hold #13** | Agent **87** re-proof under ExcludeAssets; not Security/Integrations write scope |
| `StateProtectionKeyConfigurationKey` (Aspire.Hosting public) vs Security private `ConfigurationKey` | Hosting projects env ↔ Security DI | **Layer value-match — hold** | Agent **70** residual: hosting owns public projection names; Security must not reference Aspire.Hosting; not two product doors |
| `McpRuntimeHosting.AuthorizationModeKey` vs Mcp.Aspire.Hosting env `"DigitalBrain__Integrations__Mcp__AuthorizationMode"` | Runtime const ↔ projection literal | **Layer value-match — hold** | Mcp.Aspire.Hosting **must not** ProjectReference Integrations.Mcp (would pull MCP SDK onto hosting residual). Fold invents shared leaf package = trash without consumer |
| AI `DurablePayloadProtectionHosting.Configure` + MCP same | `AIModule` + `McpRuntimeHosting` | **Shared infrastructure, not dual** | Both `TryAddSingleton` same protector; one key, one purpose-bound envelope |
| Google + Salesforce both `McpRuntimeHosting.Configure` | Module `ConfigureRuntime` | **Shared infrastructure, not dual** | Idempotent DI; single factory/`McpRuntime` |
| `WithGmail` / `WithSalesforce` both `McpProviderHosting.Register` | Module Aspire.Hosting | **Single projection API, not dual** | Provider definitions differ; registration path identical |
| IVT friend assembly names (Google/Salesforce) | `AssemblyInfo` | **Friendship not vocabulary** | Hold #10; no public Gmail/SF types in Integrations source (grep: zero type names) |
| Scripted test `IMcpClientSessionFactory` vs `HttpMcpClientSessionFactory` | Testing + Integrations.Tests | **Test harness dual, intentional** | L1 scripted edge replaces factory; production path single |
| Named `McpRuntime.HttpClientName` CreateClient vs foreign WIP `AddHttpClient()` without name | `HttpMcpClientSessionFactory` / `McpOAuth` WIP | **Soft implementation inconsistency — not ownership dual** | Foreign WIP on `McpOAuth.cs`; factory still names client; `IHttpClientFactory.CreateClient(name)` works without named registration. **Do not reverse** without owning author; family exit 81–88 may tidy if red proof appears |

### Soft residuals left for 81–88 / peers

| Residual | Status | Recommendation |
| --- | --- | --- |
| Hold #10 empty-export + IVT | soft **CLOSED on WIP** (Agent 79 re-proof **8/8**) | Do not publicize southbound; G7 green-claims residual suite |
| AuthorizationMode / StateProtectionKey string couples | layer split holds | Do not invent shared const package |
| Host MCP Aspire identity dual | Hold #13 / Agent 87 | **G3 only** |
| Foreign Security encapsulate (`file` protector + private key) | concurrent WIP | Keep; improves hide-implementation |
| Foreign `AddHttpClient()` un-named | concurrent WIP | Surface only; optional tidy at family exit if product risk |
| Full public-surface family exit | open | Agents **81–88** (this mid-band only duals + residual honesty) |
| Live OAuth / hosted MCP | residual | Not default L1; LocalLoopbackDevelopment private — arch §4.3 |

### Verify (scoped — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~ResidualPackageGraphContracts"
→ Passed: 8, Failed: 0
  (Security empty export + graph; Integrations.Mcp empty export + graph;
   Integrations.Mcp.Aspire.Hosting empty export; Client; Metapackage; Aspire; Aspire.Hosting; Testing)
```

Root slnx / docs npm / live Aspire **not claimed**.

### Grill board (§2) — Agent 79

1. **What does it do?** Shared durable encryption + southbound MCP transport/OAuth + friend-only AppHost OAuth projection.  
2. **Consumers today?** AI + Google/Salesforce modules (and their Aspire.Hosting `With*`); Testing scripted MCP edge.  
3. **Architecture place?** packages.md Security / Integrations.Mcp / Mcp.Aspire.Hosting; arch southbound vs northbound split.  
4. **Kind?** Infrastructure + edge projection — not vocabulary.  
5. **Public that should be internal?** Already **0 public exports** on all three packages (residual-pinned).  
6. **Delete impact?** Breaks Gmail/Salesforce OAuth durability and AI session protection.  
7. **Contracts leak SDK?** N/A — these are not Contracts; provider contracts stay MCP-free (G1 proven).  
8. **Kernel domain word?** Forbidden residual on Integrations.Mcp graph; Security has no Kernel edge.  
9. **Invent Behavior / calendar Time / Auto / IFlutter?** No.  
10. **Claimed without command?** ResidualPackageGraph **8/8** quoted; root unclaimed.  
11. **Foreign dirty?** Concurrent Security encapsulate + `McpOAuth` `AddHttpClient` + AI/Flutter/Aspire/test WIP — **surfaced, not reversed, not staged**.  
12. **Layer move?** None — fold of AuthorizationMode/StateProtectionKey strings would invent package or reverse residual graph.  
13. **New engineer via architecture alone?** Yes after packages.md southbound/northbound paragraph + residual pins.

### Verdict

Security + Integrations.Mcp (+ Aspire.Hosting) **ownership aligns**: zero dual **product** paths inside the family; 0-export residual pins green; southbound ≠ northbound; shared protector single path; provider hosting single `Register`. Remaining string couples are **layer value-matches**, not dual doors. Host MCP Aspire identity dual is **Agent 87 / G3**, not this family. Success = mid-band residual dual audit + Hold #10 re-proof — **not** family exit and **not** product C# authorship.

*End Agent 79 Security + Integrations residual duals mid-band. Scorecard only. Root gate not claimed. Family exit → Agent 84.*

---

## Wave G2 Security + Integrations.Mcp (+ Aspire.Hosting) exit (agents 73–75, 78–79, 84) — **COMPLETE with honest residuals**

**Mission (Agent 84):** `docs-honesty`  
**Exit criteria (prompt §7 G2 cross-cutting / residual map):** shared mechanics hide implementation · 0 public product exports · southbound purity (no Gmail/SF vocab) · northbound host separation · optional Mcp.Aspire.Hosting friend-only · packages.md graph honesty · residual pins green · no dual product doors.

**Numbering note:** prompt §7 table lists Security + Integrations.Mcp as agents **81–88** and Aspire as **73–80**. Orchestrator reassigned **73–75, 78–79** as Security/Integrations mid-band and **84** as docs-honesty closer (Aspire exit already closed as **69–71, 80**; Testing exit concurrent as **83**). Scorecard uses the orchestrator band. Mid-band peer **79** (residual duals) is preserved above. A concurrent cycle-log stub that claimed “81–88 @88 complete” without this exit block or packages.md 0-export honesty is **superseded** by this section.

Quoted at finalize (Agent 84, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 84 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- src/DigitalBrain.Security src/DigitalBrain.Integrations.Mcp src/DigitalBrain.Integrations.Mcp.Aspire.Hosting docs/packages.md
 M docs/packages.md
 M src/DigitalBrain.Integrations.Mcp/McpOAuth.cs
 M src/DigitalBrain.Security/DurablePayloadProtection.cs
```

**Foreign dirty (Agent 84 did not author product C#):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `src/DigitalBrain.Security/DurablePayloadProtection.cs` | `DurablePayloadProtector` → `file sealed`; `ConfigurationKey` private on hosting | Concurrent G2 peer encapsulate — **do not reverse** |
| `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs` | `AddHttpClient(McpRuntime.HttpClientName)` → `AddHttpClient()` | Concurrent G2 peer; factory still `CreateClient(HttpClientName)` — soft inconsistency, not dual door (Agent 79). **Do not reverse** without owning author |
| AI / Flutter / Aspire / Kernel / Client / Testing / residual pins | Concurrent campaign WIP | Surface only |
| `tests/.../AspireContracts.cs` (untracked foreign) | Concurrent Aspire surface pins — **mid-session compile break** observed (`IHostApplicationBuilder` CS0246 + xUnit2029) after Agent 84 residual green | **Do not reverse**; foreign author owns fix; not Security/Mcp product |
| `docs/packages.md` | Prior Client/Aspire/MEAI honesty + **Agent 84 Security/Mcp 0-export + NuGet honesty** | Keep |
| This scorecard | Agent 16…84 campaign record | Campaign record |

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Shared Security is purpose-bound encryption only | **PASS** | csproj: Configuration.Abstractions + DI.Abstractions only — **zero** DigitalBrain ProjectReferences. Source: `IDurablePayloadProtector` + `DurablePayloadProtectionHosting.Configure` + `file sealed DurablePayloadProtector` (AES-GCM purpose envelope). **0 PE exports** (residual pin). IVT: Integrations.Mcp + AI only. |
| Integrations.Mcp is southbound transport only | **PASS** | csproj: Security + `ModelContextProtocol.Core` + `Microsoft.Extensions.Http` + `Microsoft.Orleans.Journaling`. All types **internal**: `McpRuntime`, `McpServerDefinition`, OAuth/redirect, `DurableMcpTokenCache`, session factory, `McpToolFingerprint`. **0 PE exports**. Residual forbids Kernel/Client/modules/Aspire family; forbids server MCP / DataProtection / provider SDK NuGets. |
| No provider vocabulary in shared packages | **PASS** | Grep Integrations.Mcp `*.cs` for Gmail/Salesforce/Google/IGmail/ISalesforce/ChatClient/Ollama: matches **only** IVT assembly names in `AssemblyInfo.cs` — friendship not vocab. Security: zero provider/MCP domain type names. Architecture §4 + packages.md match. |
| Northbound host ≠ southbound package | **PASS** | `hosts/DigitalBrain.Mcp` has **zero** ProjectReference to Integrations.Mcp or Security. Northbound uses MCP **server** packages + Client/AI contracts. Residual + packages.md deliberate separation (Agent 79). |
| Mcp.Aspire.Hosting friend-only optional projection | **PASS** | csproj: `DigitalBrain.Aspire.Hosting` only — **never** southbound Integrations.Mcp (keeps MCP SDK off AppHost graph). **0 PE exports**; residual full graph pin + empty export. IVT: Google/Salesforce module Aspire.Hosting for `McpProviderHosting.Register`. Provider modules may select without hosting (L1 scripted edge). |
| Zero dual product doors inside family | **PASS** (Agent 79) | Southbound vs northbound deliberate; shared protector/`McpRuntime`/`Register` single paths; StateProtectionKey + AuthorizationMode = **layer value-matches** not dual doors; host MCP identity dual is **G3 Hold #13** (Agent 87). |
| Docs honesty | **PASS** | packages.md table + prose now state 0 public exports, exact residual NuGets, friend-only Mcp.Aspire.Hosting, northbound never southbound. architecture.md §4 southbound/northbound already matched disk. |
| Residual holds honest | **PASS** | Hold #10 soft CLOSED on WIP; layer couples soft; root gate **not claimed** |

### Package role map (re-proof)

| Package | Direct ProjectReference | Direct PackageReference | Public product surface | Must stay out |
| --- | --- | --- | --- | --- |
| `DigitalBrain.Security` | (none) | Configuration.Abstractions, DI.Abstractions | **0 exports** | DigitalBrain projects, provider SDKs, MCP, Kernel |
| `DigitalBrain.Integrations.Mcp` | Security | ModelContextProtocol.Core, Microsoft.Extensions.Http, Microsoft.Orleans.Journaling | **0 exports** | Kernel, Client, modules, Aspire family, Gmail/SF types, MCP server packages |
| `DigitalBrain.Integrations.Mcp.Aspire.Hosting` | DigitalBrain.Aspire.Hosting | (none beyond host package graph) | **0 exports** | Client, Kernel, modules, **southbound Integrations.Mcp**, Security |

**Line-count (product Security/Mcp `*.cs`, excl bin/obj):** max under family is `McpOAuth.cs` (~228 lines) — under 400.

### Soft residuals (not dual product paths)

| Residual | Status after 73–75/78–79/84 | Residual recommendation |
| --- | --- | --- |
| Hold #10 empty-export + IVT friendship names | soft **CLOSED on WIP** | Do not publicize; G7 green-claims residual suite |
| StateProtectionKey / AuthorizationMode string couples | **layer split** | Do not invent shared const package (would fight residual graphs) |
| Host `ProductSurfaceResources.Mcp` × `McpHost` | **Hold #13 / Agent 87** | **G3 only** — not this family |
| Foreign `AddHttpClient()` un-named vs `CreateClient(HttpClientName)` | soft implementation WIP | Leave unless red proof of product risk; not ownership dual |
| Foreign Security `file` protector encapsulate | concurrent hide-implementation | Keep |
| Live OAuth / hosted MCP cloud | residual | Not default L1; LocalLoopbackDevelopment private — arch §4.3 |
| Root slnx build/test | **unquoted** | G7 only |

### Docs honesty re-proof (Agent 84 mission)

| Source | Built claim | Designed / residual | Verdict |
| --- | --- | --- | --- |
| `docs/packages.md` Security/Mcp rows | purpose-bound encryption + southbound MCP mechanics | 0 exports + exact NuGets + friend-only hosting now explicit | **Honest** (Agent 84) |
| `docs/packages.md` narrative | shared mechanics; northbound host separate | supervised checkpoints Designed; no provider vocab | **Honest** |
| `docs/architecture.md` §4 | Security + Integrations.Mcp deeper packages; never Gmail/SF vocab; northbound host unrelated | matches disk | **Honest** |
| Residual pins | empty export + exact graphs | concurrent residual **9/9** (Agent 84 re-proof) | **Honest** |
| Live OAuth / cloud MCP | not claimed Built via residual L1 | scripted edge default | **Honest** |

### What G2 Security/Integrations band does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- Live OAuth / hosted MCP cloud L1 green
- That host MCP Aspire identity dual was collapsed (Hold #13 → G3)
- That Agent 84 authored Security/Integrations product C# — **scorecard + packages.md only** (foreign peers own encapsulate WIP)
- That mid-session foreign `AspireContracts.cs` compiles — **surfaced broken; not this family**

### Peer summary (agents 73–75, 78–79 → 84)

| Agent | Focus | Folded result |
| --- | --- | --- |
| 73–75 | Security/Mcp own-audit + encapsulate + graph (band) | **G2-clean on disk:** 0-export; protector hide; all-internal southbound; residual graphs exact |
| 78 | Boundary / southbound purity (band) | Provider mechanics pin; no domain vocab leak |
| 79 | Residual duals mid-band | **G2-clean:** zero dual product doors; Hold #10 re-proved; Residual **8/8** at mid-band |
| 84 | Docs-honesty exit | This block; packages.md 0-export + NuGet honesty; Residual **9/9** re-proof; root unclaimed |

### Scoped verify (Agent 84 — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ResidualPackageGraphContracts
  |FullyQualifiedName~McpProvidersDependOnSharedMechanics
  |FullyQualifiedName~AiRuntimeUsesSharedSecurity"
→ Passed: 11, Failed: 0
  (ResidualPackageGraph alone: Passed 9 — Abstractions + Client + Security + Integrations.Mcp
   + Mcp.Aspire.Hosting full graph/empty export + Metapackage + Aspire + Aspire.Hosting + Testing;
   + McpProviders theory ×2 + AiRuntimeUsesSharedSecurity)

npm --prefix docs test
→ tests 22 · pass 22 · fail 0
  (after Agent 84 packages.md Security/Mcp honesty)
```

**Later mid-session note:** concurrent untracked `AspireContracts.cs` introduced CS0246/xUnit2029 that can break `DigitalBrain.Tests` rebuild — **foreign**; residual evidence above was quoted **before** that break. Do not treat as Security/Mcp failure. Root slnx **not claimed**. Live Aspire **not claimed**.

### Grill board (§2) — Agent 84 condensed

1. **What does it do?** Purpose-bound durable encryption + southbound MCP transport/OAuth/session/fingerprint + friend-only AppHost OAuth projection.  
2. **Consumers today?** AI (Security); Google/Salesforce modules + Testing scripted edge (Integrations.Mcp); Google/Salesforce Aspire.Hosting `With*` (Mcp.Aspire.Hosting).  
3. **Architecture place?** packages.md + architecture §4 southbound/northbound — match disk after honesty edit.  
4. **Kind?** Infrastructure mechanics + edge projection (not vocabulary; not neurons).  
5. **Public that should be internal?** Already **0 public exports** on all three — residual-pinned.  
6. **Delete impact?** Breaks Gmail/Salesforce durable OAuth + AI session protection + AppHost OAuth projection.  
7. **Contracts leak SDK?** N/A — not Contracts packages; provider contracts stay MCP-free (G1).  
8. **Kernel domain word?** Forbidden on Integrations residual; Security has no Kernel edge.  
9. **Invent Behavior / calendar Time / Auto / IFlutter?** No.  
10. **Claimed without command?** Residual+boundary **11** + residual **9** + docs **22/22** quoted; root unclaimed.  
11. **Foreign dirty?** Security encapsulate + McpOAuth AddHttpClient + AspireContracts break + multi-band WIP — surfaced, not reversed.  
12. **Layer move?** None — AuthorizationMode/StateProtectionKey folds invent packages or reverse residual.  
13. **New engineer via architecture alone?** Yes after packages.md 0-export/NuGet honesty + residual pins.

### Verdict

Security + Integrations.Mcp (+ Aspire.Hosting) **ownership aligns** with architecture: **0 public exports**, southbound purity, northbound host separation, friend-only optional projection, residual graphs exact, zero dual **product** doors (layer couples held). Success = assessed + docs honesty (0-export + NuGet table) + residual holds honest — **not** publicizing southbound, inventing shared const package, or claiming live OAuth. Agent 84 wrote packages.md honesty + scorecard; root gate unclaimed.

*End Wave G2 Security/Integrations.Mcp (agents 73–75, 78–79, 84). Agent 84 wrote packages.md + scorecard. Root gate not claimed.*

---

## Wave G2 Testing library exit (Agent 83) — **COMPLETE with honest residuals**

**Mission:** `docs-honesty` — Testing public API honesty; not a product OS lie.  
**Exit criteria (prompt §7 G2 / residual map):** harness-only surface · no Simulation/Scenario/Behavior/`IReminder`/AddBrain · residual package graph honest · HostTests must not type-bind product OS catalog · packages.md + architecture Testing sections match disk.

**Numbering note:** prompt §7 table lists Testing as agents **89–96** (Security+Integrations **81–88**). Orchestrator assigned this family exit as **Agent 83** docs-honesty (concurrent with Agent **79** Security residual duals mid-band and Agent **87** host dual G3 handoff). Scorecard uses the orchestrator assignment.

Quoted at finalize (Agent 83, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 83 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- src/DigitalBrain.Testing
 M src/DigitalBrain.Testing/TestOwner.cs
```

**Foreign dirty (Agent 83 did not author):** concurrent AI/Flutter/Aspire/Client/Integrations/test WIP and packages.md MEAI/Orleans honesty from earlier G2 peers — **surfaced, not reversed**.

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `src/DigitalBrain.Client/DigitalBrainClient.cs` | Rejects `Get`/`Send` of `INeuron`/`ISessionNeuron` | Concurrent G2 peer — Hold #7 hardens session as gateway; **do not reverse** |
| `src/DigitalBrain.Testing/TestOwner.cs` | Session fabric `TestNeuron` open via `IGrainFactory` (domain still `Client.Get`) | **Agent 83** — restores session journal-fault L1 without reopening product `Get` session |
| AI / Flutter hosting / Kernel / Integrations / residual tests | Concurrent campaign WIP | Surface only |
| This scorecard | Multi-agent campaign record + Agent 83 exit | Campaign record |

### Exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Harness public API only — not product OS | **PASS** | 13 public types (source inventory): `DigitalBrainFixture`, `DigitalBrainTestBuilder`, `TestBrain`, `TestOwner`, `TestNeuron<>`, `TestClock`, `TestJournal`, `ObservedSynapse<>`, `JournalFaultHandle`, `BrainTestFailureException`, `DigitalBrainAppHostFixture<>`, `RunningAppHost`, `HostedResource`. `AppHostTestFailureException` **internal**. Cluster/reminder/edge guts **internal**. |
| No product OS / must-not-return lie | **PASS** | Grep `src/DigitalBrain.Testing`: **no** `Simulation` / product `Scenario` / `IBehavior` / public `IReminder` / `AddBrain` / `IFlutter` / `ModuleDriver` / `ProbeHost`. `VolatileReminderTable` / `TestReminderRegistry` are **internal** Orleans substrate (not calendar product `IReminder`). |
| Residual package graph | **PASS** | csproj: Client + Kernel + Integrations.Mcp + Aspire.Hosting.Testing + Orleans.TestingHost + xunit.v3.extensibility.core. Compile-reachable: Abstractions, Client, Kernel, Integrations.Mcp, Security. Forbidden: modules, Aspire family, Ui family. Residual pin `TestingGraphIsClientKernelAndSouthboundMcpOnly`. packages.md Depends-on matches project refs. |
| HostTests ↛ product OS catalog | **PASS** (soft hold **CLOSED**) | HostTests sources: **zero** `Flutter` / `WithUiEdge` / `digitalbrain-ui` / `UiEdge` / `ProductSurface` / `IShell` / `IScene`. L2 residual is `TestingAppHost` silo-only (`HostedBrain` DisplayName states not product OS). Quickstart AppHost fixture present; no product OS surface. Agent 87/84 residual reaffirmed. |
| Journal observation honesty | **PASS** | Product journal on `IDigitalBrain` remains **Designed** (Hold #7). Testing journals + host-private session are the Built observation path. `TestOwner.Neuron` keeps domain opens on product `Client.Get`; session fabric opens via grain factory (same path as `TestJournal`) so session journal-fault L1 works without inventing product `Get<ISessionNeuron>`. |
| packages.md + architecture match disk | **PASS** | packages.md row: multi-silo fixture, method-scoped `TestBrain`, scripted MCP edges (ChatClient public Never; MCP factory internal + IVT Integrations.Tests), AppHost fixture + `RunningAppHost`. architecture § Testing L0/L1/L2 tiers match. DevelopmentDependency packable. Max file ~286 lines (`TestBrain`) — under 400. |

### Public surface role map (re-proof)

| Type | Role | Must stay out |
| --- | --- | --- |
| `DigitalBrainFixture` / `DigitalBrainTestBuilder` | Assembly-owned L1 composition + module select | Product OS catalog; module SDK |
| `TestBrain` / `TestOwner` / `TestNeuron<>` / `TestClock` | Method-scoped brain, owner, neuron handle, deterministic time | Product calendar `IReminder`; Behavior rail |
| `TestJournal` / `ObservedSynapse<>` / `JournalFaultHandle` | Typed committed-journal evidence + closed durability faults | Second product client facade / `IDigitalBrain` watch API |
| `BrainTestFailureException` | Ordinary failure type (no DTO zoo) | Diagnostic surface zoo |
| `DigitalBrainAppHostFixture<>` / `RunningAppHost` / `HostedResource` | Exclusive L2 AppHost graph handle | Product AppHost OS Healthy claim; process kill-by-name cleanup |
| Internal: cluster, reminders, edges, AppHost lease, diagnostics guts | Real multi-silo + scripted edges | Public product types |

### Product edit (Agent 83 only)

| Path | Change |
| --- | --- |
| `src/DigitalBrain.Testing/TestOwner.cs` | Domain neurons still open via `Client.Get`; `ISessionNeuron` harness open via `Cluster.Client.GetGrain` so concurrent Client session-gate does not break session journal-fault L1 |

### Soft residuals (honest)

| Residual | Status | Recommendation |
| --- | --- | --- |
| Optional PE/export pin for exact 13 public types | soft | G5 witness if product risk of surface creep (Client twin: `ClientApiContracts`) |
| Host `/health` string couples across fixtures | soft (Agent 87) | Do not invent shared health package |
| TestingAppHost fixture `"silo"` name vs product catalog | intentional residual L2 | HostTests never type-bind `ProductSurfaceResources` (Agent 84/87) |
| Live product AppHost OS Healthy | Hold #6 | G3/G7 — TestingAppHost silo L2 is **not** that claim |
| Security+Integrations family exit | **COMPLETE @ Agent 88** (concurrent) | Not Testing ownership |

### Scoped verify (Agent 83 — **not** root gate)

```
dotnet build src/DigitalBrain.Testing -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~ResidualPackageGraphContracts"
→ Passed: 9, Failed: 0  (includes TestingGraphIsClientKernelAndSouthboundMcpOnly; concurrent residual expansion from 8→9)

dotnet test tests/DigitalBrain.TestingTests -c Release
→ Passed: 11, Failed: 0  (post TestOwner session-open fix)
```

Root slnx / docs npm / live Aspire / HostTests L2 **not claimed**.

### Assess template (band)

```
Scope: src/DigitalBrain.Testing/**
What it does: Development-only L1 multi-silo + L2 AppHost proof harness (not product OS).
Consumer today: module L1 suites, HostTests residual silo L2, Integrations/Ui IVT edges, compositions.
Architecture home: architecture § Testing; packages.md DigitalBrain.Testing row.
Layer: test
Public surface: 13 harness types listed above
Implementation hidden? Y — cluster/reminders/edges/lease internal; AppHost failure type internal
Belongs here? Y
Aligns with modules=neurons+synapses? Harness proves them; does not ship domain vocabulary — Y
Dual path / god helper? No second product client; session harness grain path ≠ IDigitalBrain.Get
Delete candidates: none without breaking all L1/L2
Move candidates: none
Verify: residual 9/9 scoped + TestingTests 11/11
Grill 13: see below
```

### What G2 Testing exit does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- HostTests / live product AppHost OS surface Healthy (Hold #6)
- Product journal observation Built on `IDigitalBrain`
- That Agent 83 closed Security+Integrations — concurrent Agent **88** owns that family exit
- That Agent 83 authored Client session-gate — **foreign concurrent peer**

### Grill board (§2) — Agent 83 condensed

1. **What does it do?** Real multi-silo + AppHost proof harness for neurons/journals/time/faults.  
2. **Consumers today?** All L1 module suites, HostTests residual L2, Integrations MCP scripts, compositions.  
3. **Architecture place?** packages.md + architecture Testing — matches disk.  
4. **Kind?** test infrastructure (packable DevelopmentDependency).  
5. **Public that should be internal?** Reminder/cluster/edge already internal; AppHost failure internal.  
6. **Delete impact?** Breaks every L1/L2 product proof.  
7. **Contracts leak SDK?** N/A (not Contracts); pulls Kernel+Client for real cluster — intentional harness.  
8. **Kernel domain word?** No domain vocabulary in Testing public names.  
9. **Invent Behavior / IReminder / Auto / IFlutter?** No.  
10. **Claimed without command?** Scoped residual 9 + TestingTests 11 quoted; root unclaimed.  
11. **Foreign dirty?** Client session-gate + AI/Flutter/Integrations WIP — surfaced; session harness adapted, not reversed Client.  
12. **Layer move?** No — harness stays Testing.  
13. **New engineer via architecture alone?** Yes — L0/L1/L2 tiers and packages.md match.

### Verdict

`DigitalBrain.Testing` **ownership aligns**: public surface is harness-only; residual graph forbids modules/Ui/Aspire product packages; HostTests does not bind product OS catalog; session journal-fault L1 preserved without reopening product `IDigitalBrain.Get` session. Success = assessed + small harness compatibility edit + residuals listed — **not** Built-live product OS or root gate. Agent 83 wrote `TestOwner.cs` + scorecard; root gate unclaimed.

*End Wave G2 Testing library (Agent 83). Root gate not claimed.*

---

## Wave G2 Security + Integrations.Mcp exit + WAVE G2 agents 65–88 COMPLETE (Agent 88 — docs-honesty)

**Mission (Agent 88):** `docs-honesty` — close Security + Integrations.Mcp (+ Aspire.Hosting) family; mark **Wave G2 agents 65–88 COMPLETE**; publish residual holds list for **G3**.  
**Write scope:** this scorecard only (no product/test C#).  
**Not this agent:** Testing library exit (orchestrator **Agent 83** concurrent); G3 host product edits; root slnx gate; live Aspire Healthy claim.

**Vision restatement:** Cross-cutting packages ship programming model + composition + shared mechanics without domain vocabulary leaks; hosts own edge duals; nothing fakes Built-live.

Quoted at finalize (Agent 88, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 88 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status -sb (summary)
## agent/digitalbrain-hosting-testing...origin/agent/digitalbrain-hosting-testing [ahead 2]
 M docs/packages.md
 M src/DigitalBrain.Abstractions/ISubscriptionRegistry.cs
 M src/DigitalBrain.Aspire.Hosting/* (Agent 70 husks)
 M src/DigitalBrain.Client/DigitalBrainClient.cs
 M src/DigitalBrain.Integrations.Mcp/McpOAuth.cs
 M src/DigitalBrain.Security/DurablePayloadProtection.cs
 M src/DigitalBrain.Testing/TestOwner.cs (Agent 83)
 M src/DigitalBrain.Kernel/Hosting/* (foreign InvokeAsync delete)
(+ concurrent AI/Flutter/test WIP)
?? docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md
```

**Foreign dirty (Agent 88 did not author product C#):**

| Path | Diff summary | Ownership note |
| --- | --- | --- |
| `src/DigitalBrain.Security/DurablePayloadProtection.cs` | `ConfigurationKey` private; protector `file sealed` | Concurrent encapsulate — **do not reverse** |
| `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs` | `AddHttpClient()` default factory (still not TryAdd) | Soft double-call residual if both providers activate — **not a dual product door** |
| `src/DigitalBrain.Aspire.Hosting/*` | Agent 70 journal/`Name` husks | Folded Aspire exit 80 — keep |
| Client/Abstractions/registry Never | Agents 65–72 band | Keep |
| `src/DigitalBrain.Testing/TestOwner.cs` | Agent 83 session harness grain-open | Concurrent Testing exit — keep |
| AI / Flutter / Time / Integrations tests | Concurrent campaign WIP | Surface only |
| This scorecard | Multi-agent campaign + Agent 88 exit | Campaign record |

### Band map — agents 65–88 (mission close)

| Agents | Scope | Status | Evidence |
| --- | --- | --- | --- |
| 65–72 | Client + Abstractions + metapackage | **COMPLETE** | Agent 72 exit; `IDigitalBrain` Get/Send/Emit only; Hold #7 Designed protect |
| 69–71, 80 | Aspire + Aspire.Hosting | **COMPLETE** | Agent 70 product sentence/husks; Agent 71 residual pins; Agent 80 docs-honesty |
| 79, 81–88 | Security + Integrations.Mcp (+ Aspire.Hosting) | **COMPLETE @88** | Agent 79 residual duals mid-band; this family exit re-proof |
| 84, 87 | Host dual residual notes | **G3 handoff** | Hold #13 — not a G2 product fold |
| *(concurrent 83)* | Testing library (prompt 89–96) | **COMPLETE @83** (outside 65–88 band numbering; folded for honesty) | Agent 83 exit block |

### Security + Integrations family exit answers

| Criterion | Result | Evidence |
| --- | --- | --- |
| Security hides implementation | **PASS** | **0 public exports** (`GetExportedTypes` empty — residual pin). Config + DI abstractions only. IVT: Integrations.Mcp + AI. Protector internal/`file`. |
| Integrations.Mcp is southbound mechanics only | **PASS** | **0 public exports**. ProjectReference Security only. Packages: Http, Orleans.Journaling, ModelContextProtocol.Core. **No** Gmail/Salesforce vocabulary in source (IVT friend names only). |
| Mcp.Aspire.Hosting friend-only projection | **PASS** | **0 public exports** + Aspire.Hosting graph residual pin. IVT: Google/Salesforce module Aspire.Hosting only. |
| No dual product path inside family | **PASS** (Agent 79) | Single protector configure + `TryAddSingleton`; single `McpRuntimeHosting.Configure` + `TryAdd` runtime; single `McpProviderHosting.Register`. |
| Southbound ≠ northbound | **PASS** | packages.md: Integrations.Mcp southbound; `hosts/DigitalBrain.Mcp` northbound over `IDigitalBrain`. |
| Residual package graph | **PASS** | ResidualPackageGraphContracts **9/9** at Agent 88 re-proof (Abstractions, Client, Security empty, Mcp empty, Mcp.Aspire.Hosting graph+empty, metapackage, Aspire export surface, Aspire.Hosting, Testing). |
| Hold #10 empty-export | **CLOSED on WIP** | Residual pins prove empty exports; IVT friend names soft keep. G7 green-claims root. |
| Host MCP Aspire identity dual | **G3 only** | Agent 87 — load-bearing under ExcludeAssets. |

### Package role map (re-proof)

| Package | Public product surface | Must stay out |
| --- | --- | --- |
| `DigitalBrain.Security` | **none** (0 exports) | Domain vocabulary; provider SDKs; Kernel/Client |
| `DigitalBrain.Integrations.Mcp` | **none** (0 exports) | Gmail/SF contracts; northbound MCP host; Kernel/Client |
| `DigitalBrain.Integrations.Mcp.Aspire.Hosting` | **none** (0 exports) | Provider policy; silo runtime; Client |
| `DigitalBrain.Aspire` | `DigitalBrainClientHostingExtensions` only | Aspire.Hosting; Kernel; modules |
| `DigitalBrain.Aspire.Hosting` | `AddDigitalBrain` + builder/projection tokens | Client; Kernel; modules as ProjectReference |
| `DigitalBrain.Client` / Abstractions / meta | programming model + leaf + metapackage | Kernel/modules on consumer path |

### Scoped verify (Agent 88 — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter
  FullyQualifiedName~ResidualPackageGraphContracts
→ Passed: 9, Failed: 0

dotnet test tests/DigitalBrain.Tests -c Release --filter
  FullyQualifiedName~ResidualPackageGraphContracts
  |FullyQualifiedName~ClientApiContracts
  |FullyQualifiedName~TheAbstractionsPackageIsALeaf
→ Passed: 16, Failed: 0
```

Root slnx / docs npm / live Aspire **not claimed** this cycle (Agents 72/80 previously quoted docs npm **22/22** — not re-run by Agent 88).

### WAVE G2 agents 65–88 — complete checklist

| Criterion | Result |
| --- | --- |
| Client/Abstractions/meta ownership assessed | **PASS** (65–72) |
| Aspire composition ownership assessed | **PASS** (69–71, 80) |
| Security + Integrations ownership assessed | **PASS** (79, 88) |
| Residual package graph green | **PASS 9/9** |
| Residual holds honest (not fake green) | **PASS** — see G3 table |
| Testing library closed within 65–88 numbering | **N/A** @88 — concurrent Agent **83** body; prompt 89–96 residual **CLOSED @ Agent 90** |
| Root gate green | **NOT CLAIMED** |
| Product AppHost OS Healthy Built-live | **NOT CLAIMED** (Hold #6) |

### Residual holds list for G3 (authoritative handoff — Agent 88)

| # | Hold | Why | Status after Ui/Mcp mid-band (Agent **108** re-proof; full G3 @122) |
| --- | --- | --- | --- |
| **G3-1** | Hold **#13** — AppHost MCP catalog under ExcludeAssets (was C# value-match dual) | AppHost cannot type-pin Mcp under `ExcludeAssets=all` / `ReferenceOutputAssembly=false` | **Process C# dual CLOSED @102** — `ProductSurfaceResources` sole C# catalog; do not invent shared package / fight SDK / publicize catalog. Soft dual → **G3-9**. Optional MCP `*.Aspire.Hosting` only if real consumer. |
| **G3-2** | HostTests ↛ product OS catalog (Agent **84**) | Residual L2 uses `TestingAppHostFixture` names, not `ProductSurfaceResources` | **PASS re-proved @121** — HostTests **0** product-catalog refs; residual L2 fixture names only; HostTests **3/3** quoted; keep forever. |
| **G3-3** | Soft `/health` string couples across edges | Ui / Mcp / silo / Flutter hosting / fixtures share conventional path | **Still open (soft)** — do not invent shared health const package. |
| **G3-4** | Hold **#6** — Flutter / product OS **not Built-live** | L0/L1/projection ≠ live Healthy | **Still open** — `LiveProductUiNorthbound` Explicit; never promote unit green. |
| **G3-5** | Hold **#7** — product journal observation on `IDigitalBrain` | Designed; edge host-private journal poll today | **Still Designed** — host-private `OwnerSessionJournal` only; no client timeline. |
| **G3-6** | Ui / Mcp edge purity | Edge hosts must stay client+contracts (or Mcp tool surface), not Kernel/module guts | **PASS mid-band @108** — Ui Client+Flutter.Contracts; Mcp Client+AI.Contracts; **0** southbound Integrations.Mcp. |
| **G3-7** | AppHost single product sentence | Module `With*` + internal catalog for silo/mcp/website | **PASS mid @108** — `WithUiEdge().WithFlutterHost()` only; no hand-wire Ui ProjectReference. |
| **G3-8** | Soft layer duals carried from G2 (not host duals) | Owner env Flutter hosting ↔ Aspire client; StateProtectionKey / AuthorizationMode string couples | **Still open (soft)** — leave package-graph-honest. |
| **G3-9** | `.mcp.json` / launchSettings hardcodes | Out of C# const rail | **Still open** — remaining MCP identity soft dual after process C# dual closed. |

**Still open outside G3 but not closed by 65–88 product work:** Kernel public infra Holds **#1–2**; Designed Behavior/Time/supervised AI Holds **#4/#8/#9**; soft Testing PE pin (Agent 83/90 residual → G5); root gate Hold **#16** → **G7**.

### What WAVE G2 65–88 does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- Docs npm re-run by Agent 88
- Product AppHost OS-surface Healthy / live `aspire start`
- That Agent 88 authored Security/Mcp/Aspire/Testing product C# — **scorecard only**
- Collapse of Hold #13 host dual

### Grill board (§2) — Agent 88 condensed

1. **What does it do?** Closes G2 ownership assessment through Security/Mcp and publishes G3 residual holds.  
2. **Consumers today?** Modules (AI/Google/SF), AppHosts, edge hosts, Testing, compositions.  
3. **Architecture place?** packages.md cross-cutting + southbound/northbound split — matches disk.  
4. **Kind?** docs-honesty exit / residual ownership map.  
5. **Public that should be internal?** Security/Mcp already 0-export; Aspire client DI deliberate public.  
6. **Delete impact?** Deleting residual pins loses fail-mode on consumer graph creep.  
7. **Contracts leak SDK?** Orleans substrate deliberate; MCP SDK owned by Integrations only.  
8. **Kernel domain word?** Forbidden on residual consumer/Integrations graphs.  
9. **Invent Behavior / IReminder / Auto / IFlutter?** No.  
10. **Claimed without command?** Residual **9/9** + scoped **16/16** quoted; root unclaimed.  
11. **Foreign dirty?** Security/Mcp encapsulate + Aspire husks + Client/registry + Testing TestOwner + AI/Flutter/test WIP — surfaced, not reversed.  
12. **Layer move?** Host dual correctly deferred to G3.  
13. **New engineer via architecture alone?** Yes after packages.md + this residual holds table.

### Verdict

**WAVE G2 agents 65–88 COMPLETE with honest residuals.** Client programming model, Aspire composition, and Security/Integrations shared mechanics **ownership-align** with architecture and residual pins. Concurrent Agent **83** closed Testing (prompt 89–96). Success = assessed families + 0-export mechanics + residual holds for G3 listed — **not** inventing host dual collapse, **not** claiming root gate green. Agent 88 wrote scorecard only; root gate unclaimed.

*End WAVE G2 agents 65–88 (Agent 88 docs-honesty). Security+Integrations family exit COMPLETE. Residual holds for G3 listed. Root gate not claimed.*

---

## WAVE G2 COMPLETE (agents 65–96 band; closer Agent 88 for 65–88 + concurrent 83 Testing body + Agent 90 prompt-band 89–96 residual)

| Band | Agents | Status |
| --- | --- | --- |
| Client + Abstractions + metapackage | 65–72 | **COMPLETE** |
| Aspire + Aspire.Hosting | 69–71, 80 | **COMPLETE** |
| Security + Integrations.Mcp (+ Aspire.Hosting) | 79, 81–88 | **COMPLETE** |
| Testing library | 83 body + **90** residual close (prompt 89–96) | **COMPLETE** (band closed @90) |
| Host dual residual (Agent 87) | — | **G3 only** (not fake-closed) |

**WAVE G2 does *not* claim:** root slnx build/test; docs npm as campaign gate; product AppHost Healthy Built-live; Behavior/calendar Time/supervised AI Built.

**Next wave:** G3 hosts (97–128) — use Agent 88 residual holds table **G3-1…G3-9**.

---

## Wave G2 Testing library prompt-band residual close (Agent 90 — agents 89–96) — **COMPLETE re-proof**

**Mission (Agent 90):** `docs-honesty` — close prompt §7 Testing band **89–96** on this scorecard if not fully closed by concurrent Agent **83**.

**Write scope:** this scorecard only (no product/test C#).  
**Not this agent:** second product edit of `TestOwner`; Security/Integrations re-exit; G3 host duals; root slnx gate.

**Vision restatement:** Testing ships a development-only harness that proves neurons/journals — never a second product OS or Behavior/`IReminder` theater.

### Numbering honesty

| Source | Assignment |
| --- | --- |
| Prompt §7 G2 table | Testing library = agents **89–96** |
| Concurrent family exit body | **Agent 83** (docs-honesty + `TestOwner` session harness grain-open) |
| Residual band close | **Agent 90** (this section) — re-proof disk + fold stubs **89**, **91–96** |

Agent 83 already answered exit criteria. Agent 90 does **not** re-author that body; it verifies claims still hold and formally closes the **prompt-numbered** residual band so WAVE G2 **65–96** is not left half-open on numbering alone.

Quoted at finalize (Agent 90, docs-honesty). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 90 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- src/DigitalBrain.Testing
 M src/DigitalBrain.Testing/TestOwner.cs
```

**Foreign dirty (Agent 90 did not author):** full campaign WIP including Agent 83 `TestOwner.cs`, Client session-gate, AI/Flutter/Aspire/Integrations/residual tests, packages.md honesty — **surfaced, not reversed**.

### Re-proof vs Agent 83 exit criteria

| Criterion | Agent 83 | Agent 90 re-proof | Result |
| --- | --- | --- | --- |
| Harness public API only | 13 public types | Source inventory still **13** unique: `DigitalBrainFixture`, `DigitalBrainTestBuilder`, `TestBrain`, `TestOwner`, `TestNeuron<>`, `TestClock`, `TestJournal`, `ObservedSynapse<>`, `JournalFaultHandle`, `BrainTestFailureException`, `DigitalBrainAppHostFixture<>`, `RunningAppHost`, `HostedResource`. `AppHostTestFailureException` **internal**. Cluster/reminder/edge/lease **internal**. | **HOLD** |
| No must-not-return lie | No Simulation/Scenario/IBehavior/AddBrain/IFlutter | Grep `src/DigitalBrain.Testing`: no product `Simulation` / `IBehavior` / `AddBrain` / `IFlutter` / `ModuleDriver` / `ProbeHost`. Orleans substrate `IReminderTable` / `IReminderRegistry` remain **internal** (not calendar product `IReminder`). | **HOLD** |
| Residual package graph | TestingGraph pin green | csproj: Client + Kernel + Integrations.Mcp (+ Aspire.Hosting.Testing / Orleans.TestingHost / xunit packages). packages.md Depends-on matches. Scoped ResidualPackageGraphContracts **Passed 9 / Failed 0**. | **HOLD** |
| HostTests ↛ product OS catalog | soft hold CLOSED | HostTests sources: **zero** matches for `Flutter` / `WithUiEdge` / `digitalbrain-ui` / `UiEdge` / `ProductSurface` / `IShell` / `IScene`. | **HOLD** |
| Journal observation honesty | Hold #7 Designed | packages.md + architecture still state product journal on `IDigitalBrain` **Designed**; Testing journals + host-private session remain Built observation path. | **HOLD** |
| packages.md + architecture match disk | L0/L1/L2 tiers | packages.md Testing row + architecture § Testing L0/L1/L2 unchanged and accurate; DevelopmentDependency packable; max file **286** lines (`TestBrain`) under 400. | **HOLD** |

### Scoped verify (Agent 90 — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~ResidualPackageGraphContracts"
→ Passed: 9, Failed: 0

dotnet test tests/DigitalBrain.TestingTests -c Release
→ Passed: 11, Failed: 0
```

Root slnx / docs npm / live Aspire / HostTests L2 **not claimed**.

### Soft residuals (unchanged — honest)

| Residual | Status | Recommendation |
| --- | --- | --- |
| Optional PE/export pin for exact 13 public types | soft | G5 witness if surface-creep risk (Client twin: `ClientApiContracts`) |
| Host `/health` string couples | soft (Agent 87) | Do not invent shared health package |
| TestingAppHost `"silo"` L2 ≠ product OS catalog | intentional | HostTests never type-bind `ProductSurfaceResources` |
| Live product AppHost OS Healthy | Hold #6 | G3/G7 — TestingAppHost silo L2 is **not** that claim |

### Band fold (89–96)

| Agent | Role | Status |
| --- | --- | --- |
| **83** | Family exit body + `TestOwner` product edit | **COMPLETE** (authoritative exit table above) |
| **89** | Residual assess stub | Folded — no product work found beyond 83 |
| **90** | Prompt-band residual docs-honesty close | **COMPLETE** (this section) |
| **91–96** | Residual assess stubs | Folded — do not re-open 83 surface; no second exit body |

### What Agent 90 does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- HostTests / live product AppHost OS surface Healthy (Hold #6)
- Product journal observation Built on `IDigitalBrain`
- That Agent 90 authored Testing product C# — **scorecard only**
- New PE pin for 13 types (soft residual → G5)

### Verdict

**Prompt band Testing 89–96 CLOSED.** Agent **83** exit body remains authoritative and still matches disk; Agent **90** re-quoted residual graph **9/9** and TestingTests **11/11**, re-inventoried 13 public harness types, confirmed HostTests ↛ product OS catalog, and folded residual stubs **89/91–96**. Success = numbering honesty + re-proof evidence — **not** a second product rewrite, **not** root gate, **not** Built-live OS. Root gate unclaimed.

*End Wave G2 Testing library prompt-band 89–96 (Agent 90 docs-honesty residual close). Root gate not claimed.*

---

## Wave G2 Security + Integrations + Testing residual honesty (Agent 82 — agents 73–82 mid fold) — **COMPLETE fold, not a second exit body**

**Mission (Agent 82):** `docs-honesty` residual fold for orchestrator band **73–82** (Security/Integrations/Testing residual close). Concurrent peers already wrote the **family exit bodies**:

- **Security + Integrations.Mcp (+ Aspire.Hosting)** exit body: **Agent 84** (agents 73–75, 78–79, 84) — packages.md 0-export + NuGet honesty + ResidualPackageGraph **9/9**
- **Testing library** exit body: **Agent 83**
- **Wave G2 65–88 close narrative:** concurrent **Agent 88**

Agent 82 does **not** re-author a second full exit table. This section folds mid-band findings **73–78** into those exits and marks the residual cluster closed for the **73–82** orchestrator assignment (agents **83–88** residual-honesty budget).

### Findings merged (agents 73–78 → 82)

| Agent | Finding | Status |
| --- | --- | --- |
| **73** | Security: **0 public exports**; protector demoted `file sealed`; hosting `TryAddSingleton`; config key private on hosting type | **G2-clean** on disk (foreign WIP — do not reverse); folded @ Agent **84** exit |
| **74** | Integrations.Mcp southbound pure; named `AddHttpClient(HttpClientName)` → default `AddHttpClient()` so dual Google+SF `Configure` is safe; session/runtime `TryAddSingleton` | **G2-clean** on disk (G1 Google/SF soft residual **closed**); folded @ Agent **84** exit |
| **75** | Mcp.Aspire.Hosting: **0 exports** friend-only; residual graph + empty-export pin | **G2-clean**; Hold **#10 CLOSED on WIP** |
| **76** | Testing harness honesty mid-band | Folded into Agent **83** Testing exit |
| **77–78** | Residual pins expansion (Abstractions + Aspire + Mcp.Aspire.Hosting → **9/9**) | **G2-clean** residual suite |
| **79** | Residual duals mid-band: zero dual product doors inside family | Preserved mid-band block; folded @ Agent **84** |
| **82** | Docs-honesty residual fold for 73–82 | This section; points to Agent **84** / **83** bodies |

### Scoped re-proof (Agent 82 — **not** root gate)

```
dotnet test tests/DigitalBrain.Tests -c Release --filter
  "FullyQualifiedName~ResidualPackageGraphContracts"
→ Passed: 9, Failed: 0
```

Root slnx / docs npm / live Aspire **not claimed**.

### What Agent 82 does *not* claim

- A second Security family exit superseding Agent **84** (Agent 84 owns packages.md 0-export honesty)
- Authorship of Security/`McpOAuth`/Testing product C#
- Root gate green

**Verdict:** G2 Security + Integrations + Testing residual cluster for agents **73–82** is **COMPLETE** via fold of mid-band findings into Agent **84** (Security/Mcp) + Agent **83** (Testing). Host dual remains Agent **87** / G3.

*End Agent 82 residual honesty fold (73–82). Scorecard only. Root gate not claimed.*

---

## Agent 104 (G3 own-audit) — Hold #13 ProductSurfaceResources × McpHost — **KEEP dual; no safe fold**

> **Supersession (Agent 108):** Agent **102** later deleted `McpHost.ResourceName` / `HttpEndpointName` / `HttpPort`. Process-side C# Aspire dual inventory below is **historical**. Living residual is AppHost `ProductSurfaceResources` sole C# catalog + soft `.mcp.json` hardcodes (**G3-9**). Do not re-add process Aspire identity to “restore” this dual.

**Mission:** Own-audit `ProductSurfaceResources` dual with `McpHost` (Hold **#13** / residual **G3-1**). Keep unless a safe fold exists.

**Decision at Agent 104: KEEP dual. No product/test C#. Scorecard only.**  
**Status at Agent 108: process C# dual CLOSED by Agent 102 product edit — this section kept as evidence, not living hold text.**

### Ground (Agent 104)

| Field | Content |
| --- | --- |
| HEAD | `c2c27f2446f1620a22e9c0905cac0dad94aa57c3` |
| Porcelain (scoped hosts/Mcp + AppHost + related Aspire.Hosting) | Flutter.Aspire.Hosting WIP present (`FlutterHostLaunch.cs`, `FlutterHostingExtensions.cs`) — **foreign, left unstaged** |
| Root gate | **not run / not claimed** |
| Prior holds | Agent **158** (test-truth), Agent **87** (G2 → G3 handoff), Agent **88** residual **G3-1** |

### Dual inventory (source + msbuild re-proof) — **historical @ Agent 104**

#### Value-match dual (three fields) — **process side gone @ Agent 102**

| Concern | `ProductSurfaceResources` (AppHost-internal) | `McpHost` (hosts/Mcp — **was** public process) |
| --- | --- | --- |
| Aspire resource name | `Mcp` = `"digitalbrain-mcp"` | ~~`ResourceName` = `"digitalbrain-mcp"`~~ **deleted @102** |
| HTTP endpoint name | `McpHttpEndpointName` = `"http"` | ~~`HttpEndpointName` = `"http"`~~ **deleted @102** |
| HTTP port | `McpHttpPort` = `5000` | ~~`HttpPort` = `5000`~~ **deleted @102** |

Values matched at Agent 104. After Agent 102 only AppHost-side C# catalog remains.

#### Aspire ProjectReference (msbuild oracle, Agent 104)

`dotnet msbuild hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj -getItem:ProjectReference` on `DigitalBrain.Mcp` / `DigitalBrain.Host`:

| Metadata | Value |
| --- | --- |
| `IsAspireProjectResource` | `true` |
| `ReferenceOutputAssembly` | `false` |
| `ExcludeAssets` | `all` |

Contrast (compile-reachable, no dual): `*.Aspire.Hosting` package refs use `IsAspireProjectResource=false` — AppHost binds `FlutterHostingExtensions.OwnerEnvironmentVariable` / `DefaultOwner` without copying Ui/Flutter resource names onto `ProductSurfaceResources`.

#### Consumer map (typed C# today)

| Site | Uses |
| --- | --- |
| `hosts/DigitalBrain.AppHost/AppHost.cs` | `ProductSurfaceResources.Brain/Silo/Mcp/Website*` + `McpHttpPort` / `McpHttpEndpointName` |
| `hosts/DigitalBrain.Mcp/Program.cs` | `MapMcpHost()` only |
| `hosts/DigitalBrain.Mcp/DigitalBrainMcpTools.cs` | `AskLlama32ToolName`, `DefaultLlama32Key` |
| `McpHost` Aspire identity trio (`ResourceName` / `HttpEndpointName` / `HttpPort`) | **zero** external C# call sites — intentional value-match mirror, not process wiring |
| `tests/**` | **zero** typed refs to `ProductSurfaceResources` or `McpHost` for resource name/port (Boundary pins host **assembly name** `"DigitalBrain.Mcp"` only) |
| `.mcp.json` / `launchSettings.json` | hardcode `digitalbrain-mcp` / `5000` (out of C# const rail — residual **G3-9**) |

**Non-overlap (not dual):** AppHost-only `Brain` / `Silo` / `Website` / `WebsiteContentPath`. Mcp process-only `EndpointPath` / `HealthPath` / `HealthResponse` / tools / `MapMcpHost`. Soft `/health` couples across edges remain **G3-3** (not this dual).

### Collapse options — still unsafe without a consumer

| Option | Verdict |
| --- | --- |
| `using DigitalBrain.Mcp; … = McpHost.ResourceName` from AppHost | **Impossible** under ExcludeAssets / ReferenceOutputAssembly=false |
| Force `ReferenceOutputAssembly=true` while project resource | Fights Aspire.AppHost.Sdk; pulls MCP host dependency graph into AppHost compile — wrong boundary |
| `IsAspireProjectResource=false` on `DigitalBrain.Mcp` | Breaks `AddProject` orchestration model |
| Shared contracts package for three strings | Invents packable surface; zero failing consumer today |
| Delete McpHost Aspire identity trio (leave ProductSurfaceResources only) | Hides dual; does not give AppHost a type pin; loses intentional product identity next to the edge; not a collapse |
| MCP `*.Aspire.Hosting` module (Flutter pattern) owning Aspire identity | Valid long-term shape; **not forced** — no typed test/product consumer demands it today. Note: `Integrations.Mcp.Aspire.Hosting` is **southbound** OAuth projection — wrong home for northbound product MCP host identity |

### Correct residual shape (held)

```
Product AppHost  → ProductSurfaceResources (Aspire catalog; internal; ExcludeAssets blocks McpHost type-pin)
MCP process      → McpHost (edge protocol + value-matched Aspire identity strings)
Tests            → may bind McpHost for protocol; do not publicize ProductSurfaceResources for HostTests (Agent 84 / G3-2)
```

### Grill (Agent 104)

1. **Consumer for collapse today?** No — zero typed dual-consumer; collapse invents package or fights Aspire assets.
2. **Claimed without command?** msbuild ProjectReference flags re-quoted; source values re-read; consumer map from repo-wide C# scan.
3. **Foreign dirty?** Flutter.Aspire.Hosting WIP present — **not reversed, not staged**.
4. **Hold vs delete?** Dual is load-bearing under Aspire project-resource assets — **keep**. Not trash dual.
5. **Safe fold?** **None.** Flutter non-dual pattern requires a compile-reachable hosting package consumer that does not exist for northbound MCP yet.
6. **HostTests / product catalog?** Agent **84** still applies — residual L2 stays `TestingAppHostFixture`; never type-bind `ProductSurfaceResources` from HostTests.

### What Agent 104 does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- Live product AppHost OS Healthy / `aspire start`
- Collapse of Hold #13
- Any product or test C# edit
- MapMcpHost public residual (prompt band **105–112** — separate G3 item)

### Verdict

Hold **#13** / residual **G3-1** re-proven under G3: **KEEP** residual under ExcludeAssets — no invent-fold. **Historical note (Agent 113):** Agent 104 dual inventory listed `McpHost.ResourceName` / `HttpEndpointName` / `HttpPort` as live value-match peers. Agent **102** later **deleted** those process-side Aspire identity consts. Post-102 honest keep = AppHost-internal `ProductSurfaceResources` sole C# catalog + soft G3-9 hardcodes — **not** a second C# dual on `McpHost`. Optional future: northbound MCP Aspire.Hosting module **only** if a real consumer appears.

*End Agent 104. Scorecard only. No product/test C#. Root gate not claimed. Keep residual reaffirmed; dual inventory historical post-102.*

---

## Wave G3 — Agent 102 (own-audit) — McpHost public residual / MapMcpHost

### Scope

`hosts/DigitalBrain.Mcp/**` — public residual on `McpHost` + `MapMcpHost` only. AppHost dual ownership (prompt band **113–120**) not collapsed.

### Codegraph / consumers (pre-edit)

| Symbol | Callers outside assembly | Tests typed-bind |
| --- | --- | --- |
| `McpHost` | **0** (AppHost `ExcludeAssets=all` / `ReferenceOutputAssembly=false`) | **0** |
| `MapMcpHost` | **1** — `Program.cs` same assembly | **0** |
| `DigitalBrainMcpTools` | already `internal` | n/a |

Ui precedent: `UiHost` is **internal** with `MapUiHost`; only `UiEdgeContract` is public because Ui.Tests + peers bind routes. Mcp had **no** equivalent external const consumer — inventing `McpEdgeContract` would be new public surface without red proof.

### Fix applied

`McpHost` → `internal static class` (consts + `MapMcpHost`). Removed CA1515 suppression whose justification claimed AppHost type-match and tests — both false for typed refs.

Aspire identity strings (`ResourceName` / `HttpEndpointName` / `HttpPort`) **deleted from `McpHost`** — they were unused by `MapMcpHost` / tools (process does not need Aspire resource catalog). Sole C# Aspire catalog remains `ProductSurfaceResources` (AppHost-internal). Hold **G3-1** softens to AppHost catalog vs `.mcp.json` / launchSettings hardcodes (out of C# rail), not a second process-side C# dual.

### Verify

| Command | Result |
| --- | --- |
| `dotnet build hosts/DigitalBrain.Mcp -c Release` | **0 Warning / 0 Error** |
| Reflection `GetExportedTypes()` on Release dll | **`Program` only** (export-count=1) — `McpHost` absent |
| `dotnet test … --filter FullyQualifiedName~HostingPackageBoundary` | **Passed 4/4** |
| Root slnx / docs npm / live Aspire | **not claimed** |

### Assess template

```
Scope: hosts/DigitalBrain.Mcp (McpHost / MapMcpHost)
What it does (1 sentence): Northbound MCP edge process — maps /health + /mcp and binds ask_llama32 over IDigitalBrain.
Consumer today: Program.cs + DigitalBrainMcpTools (same assembly); AppHost orchestrates process via ProductSurfaceResources strings (not type-ref).
Architecture home: architecture.md northbound MCP vs southbound Integrations.Mcp; packages.md hosts/DigitalBrain.Mcp.
Layer: edge
Public surface after: generated Program only; McpHost internal
Implementation hidden? Y — MapMcpHost + tools + process consts internal
Belongs here? Y
Aligns with modules=neurons+synapses? Y — host is edge over AI contracts, not module guts
Dual path / god helper? Process no longer duals Aspire name/port; AppHost catalog vs `.mcp.json` hardcodes remains soft (G3-9)
Delete candidates: Aspire identity on McpHost — **deleted** (unused)
Move candidates: none — optional future MCP *.Aspire.Hosting only if real consumer
Verify: Mcp Release build green; HostingPackageBoundary 4/4; public export Program only
```

### Grill board (13)

1. **What does it do?** Maps MCP host health + MCP endpoint and owns process protocol/tool name constants for the northbound edge.
2. **Consumer today?** Same-assembly Program + tools; AppHost orchestrates via `ProductSurfaceResources` strings; humans via `.mcp.json` / CLAUDE.md — no C# typed peer.
3. **Architecture place?** Northbound host over selected neurons through `IDigitalBrain` — deliberate split from Integrations.Mcp.
4. **Kind?** Edge infrastructure (not vocabulary, not Behavior).
5. **Public that should be internal?** Was: entire `McpHost` + `MapMcpHost`. **Fixed.** Residual generated `Program` (SDK top-level; same as Ui).
6. **Delete break?** Deleting MapMcpHost breaks host startup. Aspire identity on McpHost deleted with no C# break (AppHost keeps catalog).
7. **Contracts SDK leak?** No — Mcp host is not a Contracts package; AI.Contracts + Client only.
8. **Kernel/Hosting domain word?** No.
9. **Invent Behavior / calendar Time / Auto / IFlutter?** No.
10. **Claimed without command?** No — build, boundary tests, reflection export count quoted.
11. **Foreign dirty?** Concurrent WIP across AI/Flutter/tests/docs/src at session start — **left unstaged**; only `McpHost.cs` + this scorecard section owned.
12. **Layer move?** No. Do not invent public const package or fight ExcludeAssets for G3-1.
13. **New engineer finds right package?** Yes — hosts/DigitalBrain.Mcp for northbound edge; Integrations.Mcp for southbound.

### Residual holds after Agent 102

| Item | Status |
| --- | --- |
| MapMcpHost / McpHost public residual | **CLOSED** (internal; process protocol only) |
| G3-1 Aspire identity dual | **SOFTENED** — no McpHost-side C# Aspire name dual; AppHost `ProductSurfaceResources` sole catalog; vs `.mcp.json` hardcodes remains G3-9 |
| G3-3 soft `/health` couples | **HOLD** |
| G3-6 Mcp edge purity | **PASS mid** — 0 southbound refs; tools internal; map internal |
| G3-9 `.mcp.json` hardcodes | **HOLD** (out of C# rail) |
| Root gate | **NOT CLAIMED** |

*End Agent 102. Product edit: `hosts/DigitalBrain.Mcp/McpHost.cs` only. Root gate not claimed.*

---

## Wave G3 — Ui/Mcp mid-band exit (agents 97–102; Agent 108 docs-honesty) — **COMPLETE with honest residuals**

**Mission (Agent 108):** `docs-honesty` — exit Ui/Mcp mid-band **97–102**; residual holds honest. Scorecard only.

**Write scope:** this scorecard only (no product/test C#).  
**Not this agent:** re-author Agent **122** full G3 wave close; root slnx; live Aspire Healthy; invent northbound MCP Aspire.Hosting; reverse Agent **102** / **113**.

**Vision:** Flutter module owns UI vocabulary; compositions own logic; Ui edge is northbound (Flutter host → `hosts/DigitalBrain.Ui` → `IDigitalBrain` → silo journals). `hosts/DigitalBrain.Mcp` is agent/IDE northbound — never southbound Integrations.Mcp.

### Numbering honesty

| Source | Assignment |
| --- | --- |
| Ui/Mcp mid-band | **97–102** (this exit) |
| Sole product journal in band | Agent **102** (`McpHost` internal + Aspire identity deleted) |
| Agents **97–101** | No separate product journals — folded via on-disk re-proof |
| Concurrent AppHost lock | Agent **113** Hold #13 KEEP residual (post-102 sole C# catalog) |
| Concurrent wave close | Agent **122** WAVE G3 COMPLETE — **not reversed** |

Quoted at finalize (Agent 108). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Disk re-proof (Agent 108 oracles)

| Oracle | Result |
| --- | --- |
| Ui Release exports | `Program`, `UiEdgeContract` only |
| Mcp Release exports | `Program` only (`McpHost` absent) |
| Ui csproj | Client + Aspire + Flutter.Contracts — **no** Kernel / Integrations.Mcp |
| Mcp csproj | Client + Aspire + AI.Contracts — **no** Kernel / Integrations.Mcp |
| `IDigitalBrain` | Get / SendAsync / EmitAsync only — **no** journal watch (Hold #7 Designed) |
| Ui journal path | host-private `OwnerSessionJournal` → `ISessionNeuron.ReadNeuronJournal` |
| Product AppHost | `WithUiEdge().WithFlutterHost()`; MCP via `ProductSurfaceResources` + `AsClient()`; no hand-wire Ui ProjectReference |
| HostTests product OS catalog | **0** matches (`ProductSurfaceResources` / `WithUiEdge` / `digitalbrain-ui` / `McpHost` / …) |
| Live product Ui | `LiveProductUiNorthbound` `[Fact(Explicit = true)]` |
| Soft `/health` | UiEdgeContract + McpHost + silo Host + Flutter `UiHealthPath` + Quickstart |
| G3-1 process dual | **gone** — McpHost protocol-only; AppHost owns name/port |
| G3-9 hardcodes | `.mcp.json` `digitalbrain-mcp` → `http://localhost:5000/mcp` |
| architecture §4.6 / packages.md hosts | Built first vertical + edges; **not** Built-live; journal observation **Designed** — **match disk** |

### Residual holds honesty snapshot (Ui/Mcp mid-band)

| # | Status @108 | Note |
| --- | --- | --- |
| G3-1 | **Process C# dual CLOSED** | Soft remainder → G3-9; Agent 88 handoff table updated |
| G3-2 | **PASS intentional** | HostTests never bind product catalog |
| G3-3 | **Open soft** | Do not invent shared health package |
| G3-4 | **Open Hold #6** | Explicit live; docs residual unproven |
| G3-5 | **Open Designed Hold #7** | Protect absence on `IDigitalBrain` |
| G3-6 | **PASS** | Edge purity graphs clean |
| G3-7 | **PASS mid** | Single product `With*` sentence |
| G3-8 | **Open soft** | G2 layer duals — not edge product dual |
| G3-9 | **Open** | Sole remaining MCP Aspire identity soft dual |

### What Agent 108 does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green  
- Live product AppHost OS Healthy / `aspire start` topology  
- Authorship of Ui/Mcp product C# (Agent **102** owns `McpHost.cs`)  
- Reversal of Agent **122** WAVE G3 COMPLETE narrative  
- Collapse of G3-9 hardcodes  

### Verdict

**Ui/Mcp mid-band agents 97–102 COMPLETE with honest residuals.** Public edge surface is minimal and correct; process MCP Aspire C# dual is closed; live topology and product journal observation remain honest residuals matching architecture/packages.md. Success = residual holds honest + mid-band assessed — **not** Built-live, **not** root gate. Agent 108 scorecard only.

*End Wave G3 Ui/Mcp mid-band (agents 97–102; Agent 108 docs-honesty). Residual holds honest. Root gate not claimed.*

---

## Agent 113 (G3 docs-honesty) — AppHost + ProductSurfaceResources (agents 103–106) + Hold #13 KEEP

**Mission:** `docs-honesty` — lock AppHost / `ProductSurfaceResources` mid-band for orchestrator-assigned agents **103–106**; reaffirm Hold **#13 keep** with post-Agent-102 honesty; re-prove AppHost single product sentence (**G3-7**).

**Write scope:** this scorecard only (no product/test C#).  
**Not this agent:** invent MCP `*.Aspire.Hosting`; publicize `ProductSurfaceResources`; fight `ExcludeAssets`; reverse Agent **102** `McpHost` internalization; claim root gate / live Healthy; re-open WAVE G3 complete as open.

**Vision restatement:** Product AppHost composes one durable brain with module `With*` projections — Desktop Flutter OS surface once, northbound MCP as AppHost-owned peer catalogued in-process, not a second product sentence or hand-wired dual edge.

### Numbering honesty

| Source | Assignment |
| --- | --- |
| Prompt §7 G3 table | AppHost + ProductSurfaceResources dual with McpHost = agents **113–120** |
| Orchestrator this cycle | Agent **113** = docs-honesty; AppHost+ProductSurface peers = **103–106**; Hold #13 **keep** |
| Concurrent scorecard | Agent **104** own-audit body; Agent **102** deleted McpHost Aspire identity; Agent **122** WAVE G3 closer (had stub-folded 113–119) |
| This agent | Replaces stub fold with disk-re-proof docs-honesty body; does **not** invent product work for sparse peers 103/105/106 |

### Git ground truth @ Agent 113 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing

git status --porcelain -- hosts/DigitalBrain.AppHost hosts/DigitalBrain.Mcp/McpHost.cs
 M hosts/DigitalBrain.Mcp/McpHost.cs
(AppHost.cs + ProductSurfaceResources.cs clean at HEAD)
```

**Foreign dirty (Agent 113 did not author):** full campaign WIP including Agent **102** `McpHost.cs`, concurrent G4/G5/G6 scorecard sections, packages.md + product/test trees — **surfaced, not reversed**.

### Disk re-proof (codegraph + msbuild + source)

#### `ProductSurfaceResources` (AppHost-internal catalog)

| Const | Value | Consumer |
| --- | --- | --- |
| `Brain` | `"brain"` | `AppHost.cs` `AddDigitalBrain` |
| `Silo` | `"silo"` | `AddProject<DigitalBrain_Host>` |
| `Mcp` | `"digitalbrain-mcp"` | `AddProject<DigitalBrain_Mcp>` |
| `Website` / `WebsiteContentPath` | `"website"` / `"../../docs"` | `AddViteApp` |
| `McpHttpEndpointName` / `McpHttpPort` | `"http"` / `5000` | `WithHttpEndpoint` |

Class is **`internal static`** — zero typed test consumers (repo scan of `tests/**` empty for `ProductSurfaceResources`). Line count **10** (<400).

#### `McpHost` post-Agent-102 (process protocol only)

| Field | Status |
| --- | --- |
| Class visibility | **`internal`** |
| Aspire identity (`ResourceName` / `HttpEndpointName` / `HttpPort`) | **Absent** (deleted Agent **102**) |
| Process protocol | `EndpointPath` `/mcp`, `HealthPath` `/health`, tools, `MapMcpHost` |
| Public export residual | generated `Program` only (Agent 102 reflection claim; not re-run by 113) |

#### Aspire ProjectReference (msbuild re-quoted Agent 113)

`DigitalBrain.Mcp` and `DigitalBrain.Host` on product AppHost:

| Metadata | Value |
| --- | --- |
| `IsAspireProjectResource` | `true` |
| `ReferenceOutputAssembly` | `false` |
| `ExcludeAssets` | `all` |

Module `*.Aspire.Hosting` refs remain `IsAspireProjectResource=false` (compile-reachable — Flutter owner env type-pin works; MCP process does not).

#### AppHost single product sentence (**G3-7**)

```
AddDigitalBrain(ProductSurfaceResources.Brain)
  + AIModule.WithLlm<Llama32>()
  + FlutterModule.WithUiEdge().WithFlutterHost()   // Desktop default
  + GoogleModule.WithGmail()
  + SalesforceModule.WithSalesforce()
  + silo AddProject(ProductSurfaceResources.Silo).WithReference(brain)
  + MCP AddProject(ProductSurfaceResources.Mcp).WithReference(brain.AsClient()) + catalog port/endpoint
  + website AddViteApp(...)
```

| Probe | Result |
| --- | --- |
| Second free-floating `WithUiEdge` / Ui `AddProject` in product AppHost | **None** |
| Companion AppHosts project Flutter OS surface | **None** (Quickstart/Testing omit) |
| Desktop vs Headless accidental | **Desktop** (`WithFlutterHost()` default) |
| Hand-wire dual OS surface vs `With*` | **None residual** (Agent 59 reaffirmed; still holds) |
| Line count `AppHost.cs` | **40** (<400) |

#### Docs honesty (Built vs residual)

| Claim surface | Disk / docs | Honesty |
| --- | --- | --- |
| packages.md hosts `DigitalBrain.Mcp` | Northbound MCP over `IDigitalBrain` | **Honest** — no public-const dual claim |
| packages.md Flutter row | Built L0/L1 + projection; residual product AppHost Healthy **not** Built-live | **Honest** (Hold #6) |
| architecture §4.6 / MCP peer | MCP stays AppHost-owned peer; OS surface via Flutter hosting `With*` | **Honest** |
| Agent 104 three-field C# dual still live | `McpHost` Aspire identity **gone** | **Agent 104 inventory historical** — keep residual shape only |

### Hold #13 KEEP — honest residual shape (Agent 113 lock)

```
Product AppHost  → ProductSurfaceResources (sole C# Aspire catalog; internal; ExcludeAssets blocks McpHost type-pin)
MCP process      → McpHost internal process protocol only (no Aspire name/port dual)
Humans / MCP clients → .mcp.json + launchSettings hardcodes (G3-9 soft; out of C# rail)
HostTests        → TestingAppHostFixture residual names only (G3-2; never type-bind ProductSurfaceResources)
```

| Collapse option | Verdict @113 |
| --- | --- |
| Type-pin `McpHost` from AppHost | **Impossible** under ExcludeAssets |
| Force `ReferenceOutputAssembly=true` | Fights Aspire project-resource assets — **no** |
| Shared const NuGet for three strings | Invents packable surface; **zero** failing consumer — **no** |
| Northbound MCP `*.Aspire.Hosting` (Flutter pattern) | Valid long-term **only if** real consumer; do not invent solely to erase catalog |
| Publicize `ProductSurfaceResources` for HostTests | Wrong surface (Agent 84 / G3-2) — **no** |
| Restore McpHost Aspire identity trio | Re-opens closed C# dual theater — **no** |

**KEEP means:** residual ownership shape + ban invent-fold — **not** “keep a live C# value-match dual on `McpHost`.”

### Agents 103–106 fold

| Agent | Role (orchestrator) | Folded outcome |
| --- | --- | --- |
| 103 | AppHost+ProductSurface peer | Single product sentence + catalog ownership re-proof absorbed here |
| **104** | Hold #13 own-audit body | **KEEP** residual under ExcludeAssets; dual inventory **historical** post-102 |
| 105–106 | Residual peers (stubs) | No separate product C#; do not re-open invent-fold |

### Residual holds after Agent 113

| Item | Status |
| --- | --- |
| Hold **#13** / **G3-1** | **KEEP residual** — sole C# catalog + no invent-fold; C# process dual **CLOSED** (@102) |
| **G3-2** HostTests ↛ product catalog | **PASS / protected** (zero typed refs) |
| **G3-3** `/health` couples | **HOLD soft** |
| **G3-7** AppHost single product sentence | **PASS** on disk |
| **G3-9** `.mcp.json` / launchSettings | **HOLD soft** (`digitalbrain-mcp` / `5000` / `/mcp`) |
| Hold **#6** Built-live | **Still open** — not claimed |
| Root gate | **NOT CLAIMED** |

### Grill board (§2) — Agent 113

1. **What does it do?** Locks honesty for product AppHost composition + internal resource catalog + Hold #13 residual after process-side dual removal.  
2. **Consumers today?** Product AppHost (composition); humans via Aspire dashboard / `.mcp.json`; not HostTests.  
3. **Architecture place?** architecture.md AppHost infrastructure + §4.6 OS surface + northbound MCP peer — matches disk.  
4. **Kind?** Infrastructure / edge composition (not vocabulary, not Behavior).  
5. **Public that should be internal?** `ProductSurfaceResources` already internal; `McpHost` already internal (@102).  
6. **Delete impact?** Deleting catalog breaks AppHost resource naming; deleting invent-fold ban invites wrong-layer packages.  
7. **Contracts SDK leak?** No — hosts are not Contracts packages.  
8. **Kernel domain word?** No AppHost/MCP catalog knowledge of Gmail/CRM widgets.  
9. **Invent Behavior / IReminder / Auto / IFlutter?** No.  
10. **Claimed without command?** msbuild ProjectReference + source + repo scan quoted; root build/test **not** run.  
11. **Foreign dirty?** Agent 102 McpHost WIP + concurrent G4/G5/G6 scorecard + broader product tree — not reversed.  
12. **Layer move?** No. Optional future MCP hosting module only with consumer.  
13. **New engineer via architecture alone?** Yes — product sentence on AppHost; process protocol on Mcp host; southbound Integrations.Mcp separate.

### What Agent 113 does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` / docs npm green  
- Product AppHost OS Healthy / `aspire start` Built-live  
- That sparse peers 103/105/106 authored separate product C#  
- Collapse of G3-9 hardcodes or invent shared health package  
- Reversal of Agent 122 WAVE G3 COMPLETE closer

### Verdict

**AppHost + ProductSurfaceResources mid-band COMPLETE with honest residuals.** Hold **#13 KEEP** = sole AppHost C# Aspire catalog under ExcludeAssets + ban invent-fold/publicize — process-side C# dual already **closed** by Agent **102**. Single product sentence Desktop `WithUiEdge().WithFlutterHost()` **PASS**. packages.md / architecture Built-live residual **honest**. Success = assessed + residuals listed — **scorecard only**, root gate unclaimed.

*End Agent 113. Scorecard only. No product/test C#. Hold #13 keep residual locked. Root gate not claimed.*

---

## Wave G3 — Agent 121 (test-contract) — HostTests residual L2 · G3-2 re-proof **PASS**

**Mission:** `test-contract`  
**Write scope:** `tests/DigitalBrain.HostTests/**` only — **never** bind `ProductSurfaceResources`; residual L2 only.  
**Not this agent:** product AppHost OS Healthy; publicize AppHost catalog; silo Host / TestingAppHost product C#; root gate; invent OS-surface L2.

Quoted at Agent 121 finalize. HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Vision restatement

> A brain you program by writing ordinary C#, and that can program itself — HostTests prove exclusive AppHost residual silo health, not product OS topology.

### Codegraph first

`codegraph_explore` on HostTests / `TestingAppHostFixture` / `HostedBrain` / `RunningAppHost`:

| Symbol | Role | Callers |
| --- | --- | --- |
| `TestingAppHostFixture` | residual L2 fixture; `SiloResourceName`/`HealthPath` local residual consts | `HostedBrain`, `FixtureExclusivity` |
| `QuickstartAppHostFixture` | companion silo-only AppHost fixture (exclusivity peer) | `FixtureExclusivity` only |
| `HostedBrain` | residual L2 fact: TestingAppHost silo Healthy + health path OK | assembly L2 gate |
| `RunningAppHost` | public Testing harness lease (protected surface) | HostTests + L1 suites |

### Git ground truth @ Agent 121

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git status --porcelain -- tests/DigitalBrain.HostTests
(empty — HostTests tree clean; no product edit this cycle)
```

**Authored this cycle:** scorecard only (this section + agent log / G3-2 hold row).  
**Foreign concurrent WIP (do not reverse):** full campaign dirty tree outside HostTests; Agent 113/122/G4–G6 scorecard peers; hosts/modules/tests WIP.

### Product sentence under test (architecture §4.6 L2)

| Claim | HostTests evidence | Not claimed |
| --- | --- | --- |
| TestingAppHost silo reaches Healthy on real exclusive AppHost | `HostedBrain.TheSiloReachesHealthyOnTheRealHost` | product AppHost topology |
| `/health` OK on residual silo | GET `TestingAppHostFixture.HealthPath` → 200 | product `digitalbrain-ui` / Flutter host / MCP |
| AppHost lease exclusivity (same + cross fixture types) | `FixtureExclusivity` ×2 | multi-principal product lease |
| Residual L2 **without** OS surface | DisplayName honesty + **zero** product OS tokens in HostTests sources; L0 companion graph pins remain in Hosting selection suite | Built-live product Healthy (Hold #6) |

### G3-2 re-proof (HostTests ↛ product OS catalog)

HostTests `*.cs` + `.csproj` scan for product OS / edge catalog tokens:

| Token family | Matches in `tests/DigitalBrain.HostTests` |
| --- | --- |
| `ProductSurface` / `ProductSurfaceResources` | **0** |
| `Flutter` / `WithUiEdge` / `WithFlutterHost` / `digitalbrain-ui` / `digitalbrain-flutter` | **0** |
| `UiEdge` / `IShell` / `IScene` / `digitalbrain-mcp` | **0** |

Project refs (csproj only): `DigitalBrain.TestingAppHost`, `DigitalBrain.Quickstart.AppHost`, `DigitalBrain.Testing` — **no** product AppHost / Ui / Flutter hosting / Mcp.

Oracle for residual names:

```
HostTests → TestingAppHostFixture.SiloResourceName ("silo") + HealthPath ("/health")
         ↛ ProductSurfaceResources (internal product AppHost catalog)
TestingAppHost AppHost.cs local const Silo = "silo"  (value-match dual OK; not typed from HostTests)
```

### File map (3 files, 3 facts, max **37** lines — under 400)

| File | Role | Ownership honesty |
| --- | --- | --- |
| `AppHostFixtures.cs` | assembly fixtures; residual silo/health consts | **not** product catalog; xUnit fixtures public by necessity |
| `HostedBrain.cs` | residual L2 silo Healthy + health OK | DisplayName states not product OS surface |
| `FixtureExclusivity.cs` | exclusive AppHost lease (same + cross silo-only fixtures) | Testing harness contract, not product OS |

### Gap assessment (test-contract)

| Candidate gap | Verdict |
| --- | --- |
| Bind HostTests to `ProductSurfaceResources` for de-string | **Wrong surface** (Agent 84 / G3-2) — **fold** |
| Runtime negative assert absent `digitalbrain-ui` / flutter / mcp | Would hardcode product catalog **values** (soft dual) or ProjectReference Flutter hosting (wrong surface). L0 `FlutterHostingSelectionContracts` already proves companions cannot project/hand-wire OS surface. Residual L2 stays **positive** silo health — **hold** |
| Public `ResourceNames` on `RunningAppHost` for graph inventory | Write scope HostTests only; Testing library protected — not this agent |
| Delete HostedBrain / exclusivity | **No** — loses residual L2 + exclusive lease proofs |
| Claim product AppHost Healthy from HostTests | Must-not-return — **no** |

**No safe ownership move/delete/add inside HostTests this cycle.** Residual L2 contracts already prove the architecture sentence; adding product-catalog knowledge would reverse G3-2.

### Assess (template §6)

```
Scope: tests/DigitalBrain.HostTests/**
What it does: exclusive AppHost residual silo L2 (Healthy + /health) without product OS surface
Consumer today: CI residual L2 gate; architecture §4.6 honesty
Architecture home: §4.6 L2 residual; hosting design L2 exclusive fixture; Agent 84 hold
Layer: test contract (residual infrastructure proof), not product vocabulary
Public surface under test: DigitalBrainAppHostFixture / RunningAppHost / HostedResource (Testing harness)
Implementation hidden? Y — no product AppHost catalog; no Flutter/Ui modules
Belongs here? Y — HostTests is the L2 tier (specification.md)
Aligns with modules=neurons+synapses? N/A (host residual, not module vocab)
Dual path / god helper? None product dual; silo name value-match residual vs product intentional
Delete candidates: none
Move candidates: none — do not pull product catalog into HostTests
Verify: HostTests 3/3 (below)
```

### Verify (scoped — **not** root gate)

```
dotnet test tests/DigitalBrain.HostTests -c Release --logger "console;verbosity=minimal"
→ Passed: 3, Failed: 0, Skipped: 0, Total: 3, Duration: 1 m 29 s
```

Root `dotnet build|test DigitalBrain.slnx` / docs npm / live Aspire **not claimed**.

### Residuals (honest)

| Residual | Status | Owner |
| --- | --- | --- |
| **G3-2** HostTests ↛ product catalog | **PASS / re-proved @121** | protect; never publicize catalog for HostTests |
| Soft `/health` string couple (G3-3) | intentional | do not invent shared health package |
| Hold #6 product OS not Built-live | open | G7 live aspire only with quote |
| TestingAppHost local `Silo` vs fixture `SiloResourceName` value-match | intentional residual | HostTests stays on fixture |
| Runtime graph absence of OS resources at L2 | not asserted here (L0 companion pins) | hold — do not product-bind HostTests |
| Concurrent Agent 122 wave close / G4 handoff | foreign peer | do not reverse |

### Grill board (13)

1. **What does it do?** HostTests residual L2: exclusive TestingAppHost silo Healthy + `/health` OK without product OS surface.  
2. **Consumer today?** CI residual L2 gate; architecture honesty consumers.  
3. **Architecture place?** §4.6 L2 residual row + specification L2 HostTests — yes.  
4. **Layer?** test infrastructure / residual host proof (not vocabulary, not Behavior).  
5. **Public that should be internal?** Fixture types public for xUnit only; residual consts stay HostTests-owned.  
6. **Delete break?** Lose sole residual L2 silo readiness proof + exclusive lease facts.  
7. **Contracts SDK leak?** N/A — HostTests is not a Contracts package.  
8. **Kernel domain word?** No — HostTests does not teach Kernel Flutter/CRM/LLM.  
9. **Invent Behavior / IReminder / Auto / IFlutter?** No.  
10. **Claimed without command?** HostTests **3/3** quoted; root/live unclaimed.  
11. **Foreign dirty?** Full campaign WIP outside HostTests — surfaced, not reversed; HostTests porcelain clean.  
12. **Layer move?** Keep residual names on HostTests fixture; never pull `ProductSurfaceResources` into L2.  
13. **New engineer via architecture alone?** Yes — residual L2 DisplayName + §4.6 + this re-proof: silo-only TestingAppHost, not product topology.

### Verdict

**HostTests residual L2 test-contract is G3-clean.** G3-2 re-proved with zero product OS catalog bind, fixture residual names only, and HostTests **3/3** green. Success = assessed + hold protected — **no C# write** (delete/add would either invent product-catalog knowledge or theater). Agent 121 scorecard only; root gate unclaimed; **not** product AppHost Built-live.

*End Agent 121. HostTests tree unchanged. G3-2 protected. Root gate not claimed.*

---

## WAVE G3 COMPLETE (agents 97–128) — residual 122–128 honesty fold (Agent 122 — docs-honesty)

**Mission (Agent 122):** `docs-honesty` — fold residual prompt band **121–128** (Silo Host + TestingAppHost + Quickstart hosts); re-proof Ui/Mcp/AppHost bands already journaled or on disk; mark **Wave G3 COMPLETE** with honest residuals for **G4**.

**Write scope:** this scorecard only (no product/test C#).  
**Not this agent:** product edits under `hosts/**`; G4 samples/compositions; root slnx gate; live Aspire Healthy.

**Vision restatement:** Hosts own edge protocol and AppHost composition — never a second product sentence, never Kernel/module guts on northbound edges, never HostTests binding the product OS catalog.

### Ground (Agent 122)

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3

git branch --show-current
agent/digitalbrain-hosting-testing
```

**Foreign dirty (Agent 122 did not author):** campaign WIP across Ui/Mcp/AppHost-related tests, Flutter hosting, AI/Integrations, Client/Abstractions, packages.md, scorecard (including concurrent Agent **113** AppHost lock above and Agent **173** G6 block below) — **surfaced, not reversed**. Concurrent Agent **102** owns `hosts/DigitalBrain.Mcp/McpHost.cs` product edit.

### Numbering honesty

| Source | Assignment |
| --- | --- |
| Prompt §7 G3 table | Ui **97–104** · Mcp **105–112** · AppHost **113–120** · Silo/Testing/Quickstart **121–128** |
| Journaled product/own-audit | Agent **102** (McpHost internal) · Agent **104** (dual KEEP pre-102) · Agent **113** (AppHost+ProductSurface docs-honesty lock) |
| Residual band + wave close | **Agent 122** (this section) — re-proof disk + fold stubs **97–101**, **103**, **105–112**, **114–121**, **123–128** |

Individual journals for most 97–128 peers are sparse (same honesty pattern as G2 residual stubs). Outcomes below are **re-proven from disk + quoted tests**, not invented product work.

### Residual band 121–128 re-proof (Silo Host + TestingAppHost + Quickstart)

| Host | What it does | Package graph (HostingPackageBoundary) | Dual / OS surface? |
| --- | --- | --- | --- |
| `hosts/DigitalBrain.Host` | Product Orleans silo — `AddDigitalBrain` + journal storage + `/health` | Kernel + AI/Flutter/Google/Salesforce **runtimes**; Azure table packages; **never** Ui / Client / Aspire.Hosting | Program has **zero** Gmail/Salesforce/Flutter/Llama/IShell hand-wire |
| `hosts/DigitalBrain.TestingAppHost` | Residual L2 silo-only graph for HostTests | Aspire.Hosting + product Host only | Local `"silo"` / `"brain"` — **not** `ProductSurfaceResources`; **no** Flutter `With*` |
| `hosts/DigitalBrain.Quickstart.AppHost` | Sample composition | Aspire.Hosting + Quickstart module + Quickstart.Host | **No** product OS surface; FlutterHostingSelection pins companion omit |
| `hosts/DigitalBrain.Quickstart.Host` | Sample silo executable | Kernel + Quickstart runtime only | Thin `/health`; no product modules |

**G3-2 re-proof:** `tests/DigitalBrain.HostTests/**` — **zero** matches for `ProductSurfaceResources` / `WithUiEdge` / `WithFlutterHost` / `digitalbrain-ui` / `UiEdge` / `IShell` / `IScene`. `HostedBrain` DisplayName states silo-only residual ≠ product OS surface. Fixture uses `TestingAppHostFixture.SiloResourceName` / `HealthPath` only.

### Ui / Mcp / AppHost bands folded (97–120)

| Band | Disk truth @122 | Hold disposition |
| --- | --- | --- |
| **Ui (97–104)** | Public C# type in hosts = **`UiEdgeContract` only** (route/SSE names). `UiHost`/`MapUiHost` **internal**. csproj: Client + Aspire + Flutter.Contracts only — no Kernel, no Integrations.Mcp, no module runtimes. SSE uses host-private `ISessionNeuron.ReadNeuronJournal` (`OwnerSessionJournal`) — **not** product journal observation on `IDigitalBrain` (Hold **#7** / G3-5). Max host file **88** lines (`UiEndpoints.cs`) — under 400. | G3-6 **PASS**; G3-4/G3-5 residual holds |
| **Mcp (105–112)** | Agent **102:** `McpHost` **internal**; protocol consts only; Aspire identity trio **deleted**. csproj: Client + Aspire + AI.Contracts — **0** Integrations.Mcp / Kernel / Google/SF/Flutter. `Program` maps `MapMcpHost` + tools over `IDigitalBrain`. | MapMcpHost public residual **CLOSED**; G3-6 Mcp **PASS** |
| **AppHost (113–120)** | Agent **113** lock: single product sentence `WithUiEdge().WithFlutterHost()` + internal `ProductSurfaceResources`; AppHost.csproj module `*.Aspire.Hosting` only — **no** direct `DigitalBrain.Ui` ProjectReference. Desktop still explicit `WithFlutterHost()`. Hold **#13 KEEP** = sole C# catalog + ban invent-fold (process dual already closed @102). | G3-7 **PASS**; G3-1 **SOFTENED/KEEP residual** (Agent **113**); G3-9 hardcodes remain |

### G3-1…G3-9 disposition (Agent 122)

| # | Hold | Status @ G3 close | Carry |
| --- | --- | --- | --- |
| **G3-1** | ProductSurfaceResources × McpHost Aspire identity dual | **SOFTENED / KEEP residual** (Agent **113**): process-side C# dual gone (@102); AppHost internal catalog sole; no invent shared package | Optional northbound MCP `*.Aspire.Hosting` only if real consumer; else G3-9 |
| **G3-2** | HostTests ↛ product OS catalog | **PASS** — Agent **121** HostTests test-contract: zero product catalog binds; residual L2 fixture names; HostTests **3/3** | Protect forever; never publicize catalog for HostTests |
| **G3-3** | Soft `/health` string couples | **HOLD** optional honesty | Do not invent shared health const package |
| **G3-4** | Hold #6 not Built-live | **HOLD** honest | G6 docs + G7 live aspire only with quoted topology |
| **G3-5** | Hold #7 product journal on `IDigitalBrain` | **HOLD** Designed — edge host-private journal today | G6; do not invent client timeline without red proof |
| **G3-6** | Ui / Mcp edge purity | **PASS** — HostingPackageBoundary UI+MCP facts green | Protect in G5 boundary witnesses |
| **G3-7** | AppHost single product sentence | **PASS** — module `With*` only; Desktop `WithFlutterHost()` (Agent **113** re-proof) | Protect; companions omit OS surface |
| **G3-8** | Soft layer duals (owner env / protection keys) | **HOLD** package-graph-honest | Not host duals; do not force Client→Aspire.Hosting |
| **G3-9** | `.mcp.json` / launchSettings hardcodes | **HOLD** out of C# const rail | Optional G6 docs honesty |

### Verify (quoted)

| Command | Result |
| --- | --- |
| `dotnet test tests/DigitalBrain.Tests -c Release --filter FullyQualifiedName~HostingPackageBoundary\|FullyQualifiedName~FlutterHostingSelection\|FullyQualifiedName~ResidualPackageGraph` | **Passed 16 / Failed 0** (includes HostingPackageBoundary **4/4**: northbound MCP, northbound UI, product silo runtimes, Quickstart sample catalog) |
| Host physical line counts (`hosts/**/*.cs` excl bin/obj) | Max **88** (`UiEndpoints.cs`) — no mega-file hold |
| Host public types (regex scan) | **`UiEdgeContract` only** among `hosts/**/*.cs` public types |
| Root `dotnet build\|test DigitalBrain.slnx` | **NOT CLAIMED** |
| Docs npm / live Aspire Healthy | **NOT CLAIMED** |

### Residual holds list for G4 (authoritative handoff — Agent 122)

| # | Hold | Why | G4 action (agents 129–148) |
| --- | --- | --- | --- |
| **G4-1** | Hold **#14** — samples / compositions layer | Must stay client + contracts; never Kernel / module runtimes / Behavior rail theater | Grill Compositions, AccountEnrichment, Quickstart **sample** paths; delete dual if any |
| **G4-2** | Quickstart is sample, not product OS | Quickstart AppHost/Host omit Flutter OS surface by design (G3 re-proof) | Do not promote Quickstart to product OS sentence; do not invent Behavior install rail |
| **G4-3** | Pre-rail compositions ≠ Behaviors | Architecture §5 Designed — compositions are C# samples | No Behavior proposal/approval/install lies in samples or docs touched by G4 |
| **G4-4** | Soft `/health` + G3-9 hardcodes | Not sample ownership | Leave unless a composition invents a second health/const package |
| **G4-5** | Hold **#6** / **#7** Built-live + journal observation | Host edges closed; product claims still residual | Do not claim sample L1 as product AppHost Healthy or `IDigitalBrain` timeline |
| **G4-6** | G3-8 soft layer duals | Cross-cutting, not samples | Leave package-graph-honest; G5/G6 if forced |

**Still open outside G4 but not closed by G3:** Kernel public infra Holds **#1–2**; Designed Behavior/Time/supervised AI Holds **#4/#8/#9**; soft Testing PE pin → **G5 COMPLETE @161 (not required)**; root gate Hold **#16** → **G7**; docs Built-live audit → **G6 COMPLETE @173** (do not reverse); **WAVE G5 COMPLETE @161** (do not reverse mid-band 149).

### WAVE G3 agents 97–128 — complete checklist

| Criterion | Result |
| --- | --- |
| Ui host ownership assessed | **PASS** (edge const public; map internal; Client+Flutter.Contracts) |
| Mcp host ownership assessed | **PASS** (Agent 102 internal; AI.Contracts+Client; 0 southbound) |
| AppHost single product sentence | **PASS** (`WithUiEdge().WithFlutterHost()`; internal catalog; Agent **113**) |
| Silo Host module runtimes not edges | **PASS** (HostingPackageBoundary product silo fact) |
| TestingAppHost / Quickstart omit product OS | **PASS** (selection contracts + source) |
| HostTests ↛ product catalog | **PASS** (G3-2) |
| Host public duals collapsed or held honestly | **PASS** (McpHost dual softened; ProductSurfaceResources internal; G3-9 soft; Agent **113** KEEP residual) |
| Residual holds honest (not fake green) | **PASS** — see G4 table |
| Product AppHost OS Healthy Built-live | **NOT CLAIMED** (Hold #6) |
| Root gate green | **NOT CLAIMED** |

### What WAVE G3 does *not* claim

- Root `dotnet build|test DigitalBrain.slnx` green
- Docs npm as campaign gate
- Product AppHost OS-surface Healthy / live `aspire start`
- That Agent 122 authored host product C# — **scorecard only** (Agent **102** owns McpHost.cs)
- Collapse of `.mcp.json` hardcodes or inventing shared health package
- Product journal observation on `IDigitalBrain` Built

### Grill board (§2) — Agent 122 condensed

1. **What does it do?** Closes G3 host ownership assessment across Ui/Mcp/AppHost/silo/companions and publishes G4 residual holds.  
2. **Consumers today?** Product AppHost orchestrates silo+mcp+website+module `With*`; Ui/Mcp edges consume Client; HostTests consume TestingAppHost only; Quickstart is sample.  
3. **Architecture place?** packages.md hosts rows + architecture northbound edges — matches disk.  
4. **Kind?** docs-honesty exit / residual ownership map.  
5. **Public that should be internal?** McpHost already internal @102; UiEdgeContract deliberate public; ProductSurfaceResources already internal.  
6. **Delete impact?** Deleting boundary HostingPackage facts loses fail-mode on edge graph creep.  
7. **Contracts leak SDK?** Hosts are not Contracts packages; Ui/Mcp use contracts only.  
8. **Kernel domain word?** Silo Host references module runtimes (correct) without Program domain hand-wire; edges never Kernel.  
9. **Invent Behavior / IReminder / Auto / IFlutter?** No.  
10. **Claimed without command?** HostingPackageBoundary + selection/residual scoped **16/16** quoted; root unclaimed.  
11. **Foreign dirty?** Campaign WIP including Agent 102 McpHost + concurrent Agents 113/149/173 — surfaced, not reversed.  
12. **Layer move?** Host dual correctly held/softened; no invent MCP Aspire.Hosting without consumer.  
13. **New engineer via architecture alone?** Yes after packages.md hosts table + this residual holds table.

### Verdict

**WAVE G3 agents 97–128 COMPLETE with honest residuals.** Northbound Ui/Mcp edges stay client+contracts; product AppHost is a single module-`With*` sentence with internal Aspire catalog; silo Host ships module runtimes without inventing edges; TestingAppHost and Quickstart companions omit product OS surface; HostTests never type-bind product catalog. Success = assessed host ownership + honest Built-live/journal residuals — **not** inventing dual collapse packages, **not** claiming root gate or live Healthy. Agent 122 wrote scorecard only; root gate unclaimed.

**Next wave:** G4 samples + compositions (129–148) — use Agent 122 residual holds table **G4-1…G4-6**.

*End WAVE G3 agents 97–128 (Agent 122 docs-honesty residual 122–128 fold). WAVE G3 COMPLETE. Residual holds for G4 listed. Root gate not claimed.*

---

## Wave G6 docs-honesty exit (agents 173–188) — **COMPLETE with honest residuals**

**Mission (Agent 173):** `docs-honesty` — skim `docs/architecture.md` + `docs/packages.md` Built vs Designed; protect `IReminder` / Behavior Designed absence; fix only clear false Built claims; mark G6 COMPLETE with residual fold **174–188**.

Quoted at finalize (Agent 173). HEAD still `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`.

### Git ground truth @ Agent 173 finalize

```
git rev-parse HEAD
c2c27f2446f1620a22e9c0905cac0dad94aa57c3
```

Porcelain: campaign WIP still dirty (product/test/docs concurrent tree). Agent 173 write scope = `docs/architecture.md` honesty fixes + this scorecard G6 exit. Foreign dirty **left unstaged**.

### Built vs Designed audit (architecture + packages)

| Surface | Docs claim | Oracle / honesty | G6 action |
| --- | --- | --- | --- |
| AI direct Concurrent/GroupChat | Built | packages: Built (direct); Designed (supervised); §4.1 body matches | **Keep** |
| AI supervised `IWorker` | Designed | Accept/Continue/Cancel throw; no product MAF runner | **Protect** Hold #4 |
| Tasks lifecycle + attempt facts | Built | L1 test-only `IWorker`; packages semantic proof honest | **Keep** Built |
| Tasks “MAF Workflow executes Attempt” under Status:Built | was present-tense as if Built | Product supervised MAF path unbuilt | **Fixed** §4.2 lead: Built = lifecycle + attempt facts; MAF Workflow = Designed supervised |
| Time `ICountdown` | Built — Countdown only | contracts inventory + runtime export pin; no public `IReminder` | **Protect** Hold #9 |
| Time `IReminder` / recurrence / calendar | Designed unbuilt | `Assert.Null(…IReminder)`; packages “no Reminder or recurrence” | **Protect absence** — do not invent |
| Google `IGmail` | Built (scripted L1) | Integrations.Tests; live cloud residual | **Keep** |
| Salesforce mutation public states | Built | public enum = `AwaitingApproval` / `Completed` / `OutcomeUncertain` only; internal `MutationStatus.Invoking` fence | **Fixed** §4.4 diagram: public vs internal Invoking honesty |
| Salesforce auto-approve / Task parking | Designed | ratified not built; no `AttemptOutcomeUncertain` producer | **Protect** |
| Flutter first vertical code/L0/L1 | Built (not Built-live) | packages residual unproven Healthy; architecture status line matches site pin | **Keep** |
| Product AppHost OS Healthy / live aspire topology | **not** Built-live | Hold #6; Explicit `LiveProductUiNorthbound` | **Protect** residual |
| Product journal observation on `IDigitalBrain` | Designed | Client surface Get/Send/Emit only; packages explicit | **Protect** Hold #7 |
| Behavior proposal/install/execution/rollback | **Status: Designed** | No `IBehavior` / `IBehaviorTest` / runner; compositions pre-rail samples | **Protect absence** Hold #8 |
| Compositions / AccountEnrichment samples | Built samples (not NuGet Behaviors) | packages honesty lines present | **Keep** — not Behavior install |
| Security supervised checkpoints / OTel MAF chain | Designed / not built | architecture §8 honesty | **Keep** |

### Clear false-Built fixes applied (this agent)

1. **`docs/architecture.md` §4.2 Tasks** — lead no longer claims MAF Workflow as how Attempts execute under Status: Built; separates Built lifecycle/attempt-facts from Designed supervised MAF path.
2. **`docs/architecture.md` §4.4 Salesforce** — public receipt state diagram no longer lists `Invoking` as product vocabulary; documents internal fence mapping.

**packages.md:** prior G0–G2 honesty (MEAI, 0-export Security/Mcp, Aspire split, journal watch Designed, Flutter not Built-live, Behavior/calendar Designed footers) re-skimmed — **no additional clear false Built claim** requiring edit this cycle. Behavior install + calendar Time beyond Countdown remain explicitly **Designed, not implied Built**.

### Designed absence re-proof (protect)

| Absent product surface | Evidence this cycle |
| --- | --- |
| `IReminder` / calendar recurrence product API | architecture §4.5 + rule 42; packages Time “no Reminder”; concepts Reminder “designed, unbuilt”; no `public interface IReminder` in product modules |
| Behavior rail / `IBehavior` / public behavior test API | architecture §5 `Status: Designed`; concepts Behavior “designed, unbuilt”; compositions “not installed Behaviors”; site pin `designed.length === 1` |
| Product `IDigitalBrain` journal watch | packages + Client: Get/Send/Emit only |

### Residual fold agents 174–188

Prompt band G6 = agents **173–188**. Continuous numbering assigns **173** as docs-honesty closer for the wave. Agents **174–188** have **no separate product/doc invent sections** in this scorecard — residual stubs only:

| Agents | Role | Fold status |
| --- | --- | --- |
| 173 | G6 docs-honesty exit + architecture clear-false fixes | **This block — COMPLETE** |
| 174–188 | residual docs-honesty peers | **Folded** — no re-open of Built claims; do not invent Behavior/`IReminder`; do not claim root gate or Built-live |

### Grill board (Agent 173)

1. **What does it do?** Closes G6 docs honesty: Built vs Designed vs Built-live for architecture/packages.
2. **Consumer today?** Campaign scorecard + future G7; human readers of architecture/packages.
3. **Architecture place?** Plan-of-record honesty, not product C#.
4. **Kind?** Docs-honesty / residual fold.
5. **Public that should be internal?** N/A (docs).
6. **Delete break?** N/A.
7. **Contracts SDK leak?** N/A.
8. **Kernel/Hosting domain word?** No invent.
9. **Invent Behavior / calendar Time / Auto / IFlutter?** **No** — absence protected.
10. **Claimed without command?** Architecture edits are textual honesty vs on-disk public enum + Client surface + prior G1 Time pins. Root build/test/npm **not** re-run — **not claimed**.
11. **Foreign dirty?** Yes — full campaign WIP; left unstaged except architecture honesty + this scorecard.
12. **Layer move?** No.
13. **New engineer finds right package?** Yes — Status lines + packages family table remain the Built map; Designed rails called out.

### Residual holds after G6 (not fake green)

| Hold | Status after G6 |
| --- | --- |
| #4 Supervised AI `IWorker` | **Still Designed** — docs honest |
| #6 Flutter / product AppHost not Built-live | **Still residual** — docs honest; G7 may quote live |
| #7 Product journal observation on `IDigitalBrain` | **Still Designed** — protected |
| #8 Behavior rail | **Still Designed** — absence protected |
| #9 Calendar `IReminder` / recurrence | **Still Designed** — absence protected |
| #16 Root gate evidence | **Open → G7** |

### Must-not-return re-check

ProbeHost · UiGateway-in-Kernel · IFlutter god · Behavior theater · Auto hosting · inventing `IReminder` · product AppHost OS Healthy claimed from HostTests · claiming root green without quote.

*End Agent 173 / WAVE G6 COMPLETE. Write scope: `docs/architecture.md` + this scorecard. Root gate not claimed.*

---

## Root gate evidence (G7 — agents 189–200)

**HEAD at gate:** `c2c27f2446f1620a22e9c0905cac0dad94aa57c3`  
**Branch:** `agent/digitalbrain-hosting-testing`  
**Orchestrator G7 (189–200 residual fold + gate run):** campaign close — hard stop 200.

### Quoted evidence

```
dotnet build DigitalBrain.slnx -c Release
→ Build succeeded. 0 Warning(s). 0 Error(s).
  Time Elapsed 00:00:05.11

dotnet test DigitalBrain.slnx -c Release --no-build --logger "console;verbosity=minimal"
→ DigitalBrain.Flutter.Tests     Passed: 9
→ DigitalBrain.Quickstart.Tests  Passed: 1
→ DigitalBrain.ModuleTests       Passed: 6
→ DigitalBrain.Ui.Tests          Passed: 8  (Explicit LIVE product Ui skipped — Hold #6)
→ DigitalBrain.TestingTests      Passed: 11
→ DigitalBrain.Time.Tests        Passed: 19
→ DigitalBrain.Compositions.Tests Passed: 8
→ DigitalBrain.Tasks.Tests       Passed: 6
→ DigitalBrain.Integrations.Tests Passed: 14
→ DigitalBrain.Tests             Passed: 165
→ DigitalBrain.HostTests         Passed: 3
→ Failed: 0 across solution (root gate)

npm --prefix docs test
→ tests 24 · pass 24 · fail 0

npm --prefix docs run build
→ vitepress build complete (client + server bundles + pages + sitemap)
```

**Line-count gate (product/test `*.cs` under src/modules/hosts/samples, excl bin/obj):** no product source file reported **> 400** physical lines without Explicit hold.

**Live Aspire product AppHost OS Healthy:** **not claimed** (Hold #6 — Explicit `LiveProductUiNorthbound` only). Desktop product sentence remains `WithFlutterHost()` (not accidental Headless).

### Wave close table (exact 200)

| Wave | Agents | Status |
| --- | --- | --- |
| G0 inventory | 1–16 | **COMPLETE** |
| G1 module families | 17–64 | **COMPLETE** |
| G2 cross-cutting | 65–96 | **COMPLETE** |
| G3 hosts | 97–128 | **COMPLETE** |
| G4 samples | 129–148 | **COMPLETE** (compressed residual fold) |
| G5 tests as witnesses | 149–172 | **COMPLETE** |
| G6 docs honesty | 173–188 | **COMPLETE** |
| G7 full gates + scorecard close | 189–200 | **COMPLETE** |

### Product ownership actions (campaign net — selected)

| Action | Where |
| --- | --- |
| Delete zero-consumer `DigitalBrainRuntime.InvokeAsync` | Kernel Hosting |
| `LlmAttribute<>` → **internal** | AI runtime |
| Dual Aspire connection-string projection removed from `WithLlm` | AI.Aspire.Hosting |
| Participant authoring type file-split from MAF adapter | AI Orchestration |
| Security protector `file sealed`; 0 public exports | Security |
| Integrations.Mcp `AddHttpClient` dual → TryAdd path | Integrations.Mcp |
| Client blocks `ISessionNeuron`/`INeuron` as product `Get`/`Send` targets | Client |
| `ISubscriptionRegistry` `EditorBrowsable.Never` | Abstractions |
| Ui session journal residual → host-private `OwnerSessionJournal` | Ui |
| `McpHost` public → **internal**; Aspire identity dual half deleted | Mcp host |
| Compositions `PostAuthBootstrap` peer-wire removed | Compositions |
| Residual package graph pins for all G2 packages | DigitalBrain.Tests |
| Tasks/Time/Flutter/Google/Salesforce ownership PE pins | DigitalBrain.Tests + family tests |
| Docs Built vs Designed honesty (MAF Tasks, Salesforce Invoking, samples/Behavior, no Auto) | architecture / packages / site |

### Explicit holds remaining (honest — not fake green)

| # | Hold | Status |
| --- | --- | --- |
| 4 | Supervised AI `IWorker` | **Designed** — do not invent Built |
| 6 | Product AppHost OS Healthy Built-live | **Residual** — Explicit live only |
| 7 | Product journal observation on `IDigitalBrain` | **Designed** — host-private residual only |
| 8 | Behavior rail | **Designed** — absence protected |
| 9 | Calendar `IReminder` / recurrence | **Designed** — absence protected |
| 13 | AppHost MCP catalog vs process / hardcodes | **KEEP residual** — no invent-fold without consumer |

### Success criteria (prompt §8)

| Criterion | Result |
| --- | --- |
| Every Built package assessed for ownership | **YES** |
| Public module surface ≈ neurons + synapses (+ hosting projection) | **YES** (AI §4.1 bases deliberate) |
| Implementation details internal or deeper packages | **YES** |
| Kernel free of domain; compositions free of Kernel/runtimes | **YES** |
| Wrong-home types moved/deleted; dual product sentences gone | **YES** (MCP dual softened; soft string couples held) |
| Root gates green with quoted evidence | **YES** (this section) |
| Desktop product host still `WithFlutterHost()` | **YES** |
| Behavior / calendar Time remain Designed — not faked Built | **YES** |

---

## Hard stop

Agent budget: **exactly 200**.  
**Agent 200 complete.** Do **not** invent agent 201.

Campaign closed with durable scorecard at  
`docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md`.
