# Abstractions / Core / Kernel — inventory + freeze checklist

> **v1.2.1 — Grill A–E PRESENT (re-read this file; do not FAIL on stale v1.0/v1.1 context)**  
> A → §A CodeGraph honesty (line ~24) · B → §B HTML→tip map (line ~84) · C → §C Residuals + `LIVE MCP NOT RUN — seam not ratified` (line ~108) · D → §D Vlad decide (line ~127) · E → §E VerifiedActor placement (line ~144).  
> Also mirrored at `/workspace/report-extract/ABSTRACTIONS-CORE-KERNEL-INVENTORY.md`.

**Status:** Architect draft for grill **v1.2.1** (doc only — no code/PRs)  
**Date:** 2026-08-12  
**Tip:** `stage1-outcome-rail` @ `aa5dfb35`  
**Inputs:** Architect spine · Kernel Engineer (+ remainder + optional supplement) · `/workspace/report-extract/BRIEFING-FULL.md` · `/workspace/report-extract/BRIEFING.md` · `/workspace/report-extract/CODEGRAPH-HONESTY.md` · Product Grill FAIL (A–E)  
**Authority:** Vlad signoff after CoS + Product Grill. Brain/Cortex HTML = amended **TARGET**, not tip code.

---

## 0. Grill veto (GREEN ≠ GRILL)

Refactor PRs blocked until Vlad ratifies this package **and** Product Grill passes:

1. Vlad layer table (this document).  
2. CodeGraph honesty (§A — content, not a checkbox).  
3. HTML→tip mechanism map (§B).  
4. Residuals honesty (§C) including live MCP.  

`dotnet build -warnaserror` green is necessary, never sufficient. Scenario panel scores are **forbidden** as tip honesty.

---

## A. CodeGraph honesty (tip `aa5dfb35`)

Source artifact: `/workspace/report-extract/CODEGRAPH-HONESTY.md` (generated 2026-08-12T18:29:44Z). Index: 786 files / 6,988 nodes / 16,275 edges.

### A.1 Project / layer edges (intended vs suspicious)

| Edge class | Intended | Tip signal |
|---|---|---|
| Modules → Core → Abstractions | Yes | Emit/Send blast in Core Neuron* + module workers |
| Sdk → Core + Abstractions | Yes | McpAuthorizationNeuron / WebhookIngress EmitAsync callers |
| Kernel host → Core + Auth surfaces | Yes | DigitalBrainHost → `ProductModules.Assemblies` |
| **DigitalBrain.Mcp → VerifiedActor.Enter** | **Suspicious / illegal residual** | Many MCP tools mint ambient actor from string keys — bypasses HttpActor cookie mint |

### A.2 `VerifiedActor.Enter` callers (CodeGraph)

**Correct / expected**

- `Core/Filters/OwnerBoundCallFilter.cs` — Invoke  
- `Core/Neuron/NeuronTurnCoordinator.cs` — DeliverAsync (re-enter from delivery)

**Suspicious / illegal residual (northbound spoof surface)** — tip MCP tools enter with fixed/selectable `alice|bob|operator`:

- `Kernel/DigitalBrain.Mcp/ChatTools.cs` — SendChatMessageAsync, ActivateChatButtonAsync  
- `Kernel/DigitalBrain.Mcp/IntrospectionTools.cs` — ListActiveNeuronsAsync, ReadNeuronJournalAsync, ReadChatTranscriptAsync  
- `Kernel/DigitalBrain.Mcp/LibraryBehaviorTools.cs` — Publish/Discover/Install/Enable/ListLibrary*, StartRepoReviewAsync, ReadBehaviorRunAsync, ReadInstallConfigAsync  
- `Kernel/DigitalBrain.Mcp/RegistryTools.cs` — ListRegistry/RegisterInstance/InstallBundle/Grant/Revoke/ReadChart*  
- `Kernel/DigitalBrain.Mcp/TimeTools.cs` — ArmSchedule/ForceCatchUp/ReadSchedule/ReadCorpus/CellApply/CellReset  

Impact query: changing `VerifiedActor` touches **62** symbols (Core ambient + all of the above MCP Enter sites + Time ScheduleNeuron + UI ChatTurnWorker).

### A.3 Core product-domain inventory (filesystem — leakage)

Present under `src/Kernel/DigitalBrain.Core/` on tip (not interconnect-only):

- `Behavior/` — BehaviorNeuron.cs, BehaviorState.cs, StoredRun.cs  
- `Cell/` — CellNeuron.cs, CellState.cs, CalculatorKind.cs, ICellKind.cs  
- `Corpus/` — CorpusNeuron.cs, CorpusState.cs  
- `Library/` — LibraryNeuron.cs, LibraryState.cs, LibraryContent.cs  
- `Repository/` — RepositoryNeuron.cs, RepoState.cs  
- `Workspace/` — WorkspaceNeuron.cs  
- `Grants/` — GrantsNeuron.cs, GrantsState.cs  
- `Registry/` — InstanceRegistryNeuron.cs, KindRegistryNeuron.cs, *State.cs  

Interconnect-belonging siblings (keep): `Neuron/`, `Outbox/`, `Filters/`, `Identity/` (VerifiedActor*), `Capabilities/`, `Serialization/`, `Hosting/`.

### A.4 Dual-catalog proof

| Catalog | Path | Tip content |
|---|---|---|
| AppHost composition | `src/Kernel/DigitalBrain.AppHost/AppHost.cs` | `AddModule<AIModule|MemoryModule|UiModule|GoogleModule|SalesforceModule>` (+ AI voice hooks) |
| Silo ProductModules | `src/Kernel/DigitalBrain.Kernel/ProductModules.cs` → used by `DigitalBrainHost.cs` | Contracts: Abstractions graph, AI, Introspection, Memory, Execution, Time, **Chat**, Sdk.Mcp; Impl: AI, Google, Introspection, Memory, Salesforce, Execution, Time, UI, Sdk.Mcp |

Two lists must be hand-kept aligned (comment admits it). **Illegal for “cleanup PR” without single-catalog design.**

### A.5 Emit/Send blast (do not casually refactor)

CodeGraph blast: `Neuron.SendAsync` / `EmitAsync` fan into Behavior/Library/Relay/DigitalBrainNeuron, Execution*, AI SystemTools, UI ChatTurnWorker/SurfaceBoot, Sdk MCP/Webhook — **no covering tests found** (owner amendment: do not restore central suite). Spine edits require SOURCE→GRILL with live smoke, not folder moves.

---

## B. HTML → tip mechanism map

**Sources:** `/workspace/report-extract/BRIEFING-FULL.md` §D · `/workspace/report-extract/BRIEFING.md` (Architecture Trace Briefing).  
**Cortex Absorption:** companion `saved_resource.html` never committed; design substance = **MIGRATION-LOG Sessions 2–4** + Brain companion + BRIEFING-FULL. That **is** the Cortex record.  
**Forbidden:** treat Brain panel scores (100% carried / 60% as-asked) as tip honesty or acceptance. Scores are TARGET-architecture scenario readings only.

| TARGET mechanism (HTML / BRIEFING §D) | Tip @ aa5dfb35 | Gap |
|---|---|---|
| RouteOutcome / Unrouted | Present (`Abstractions/Graph/*`); journaled into **emitter incoming** post-drain | No `IInbox`; no dual addressing; **no FixPath** field (Reason-only) |
| Outcome staging outside drain | NeuronOutbox stages then commits after loop | Matches hazard control; delivery model ≠ original plan |
| `db.datum` + EffectiveAlias | Not tip-complete | TARGET addition |
| Provenance on connections | Not tip-complete | TARGET |
| Cell verbs / kinds-as-data Wave 0 | Cell contracts + Core CellNeuron exist; kinds-as-data not Wave-0 ratified | Vlad fork |
| Principal on SynapseDelivery | Partial; drain re-enter exists | A18 owner-scoped stores residual |
| `ai.ask` fireable | Not tip-standard SystemTools surface | TARGET |
| Broadcast opt-in | Tip code: `[Broadcast]` only (`BroadcastAttribute.cs`, SynapseWiring, NeuronMessagePipeline comment) | CLAUDE/UA docs still lie (`IHandle` enrolls) — **cite code** |
| Identity stamp at ingress | HttpActor cookie path correct; **MCP spoofs** alice/bob/operator | §C |
| Aspire substrate | AppHost present | Live probe not run |
| Schedule catch-up | Time module path exists | Not spine gate |
| Deliberate install / no auto-propagate | Library tools exist; sharing wording conflict | Vlad fork |
| Non-goals: OS multi-window, FS sandbox, auto cross-brain, continuous 60Hz | Align with tip non-goals | Keep as non-goals |

---

## C. Residuals (explicit — not paper-closed)

> **LIVE MCP NOT RUN — seam not ratified.**

| Residual | Tip fact | Status |
|---|---|---|
| Live MCP / Aspire principal probe | Not executed this ratification package | **LIVE MCP NOT RUN — seam not ratified** |
| Fixed principals | `ChatTools` / `LibraryBehaviorTools` / `RegistryTools` / `TimeTools` / `IntrospectionTools` accept `alice\|bob\|operator` and `VerifiedActor.Enter(...)` | Tip `@ aa5dfb35` **spoof residual** |
| Local auth branch | `fix/mcp-auth-principal` exists in some checkouts | **Unproven / not tip** — must not be cited as shipped |
| Outcome visibility | Multi-hop refusals land on hop sender; `SystemTools.fire` poller often timeout-blind; no FixPath | Open interconnect debt |
| `McpAuthorizationNeuron` | Hardcodes grain type `"chat"` + `main` via PrincipalPartition | Conversation extract landmine |
| Empty Auth project | `src/Kernel/DigitalBrain.Auth/` = bin/obj only on tip | Live auth = `Kernel/Auth/**` |
| Core AI package | `DigitalBrain.Core.csproj` references `Microsoft.Extensions.AI.Abstractions` | Smell — do not freeze ChatMessage into Core |
| Doc trap 8 | CLAUDE/UA ≠ code `[Broadcast]` | Docs-only later; inventory cites **code** |
| Dual catalog | AppHost vs ProductModules | Open |
| Core domain leakage | Behavior/Cell/Library/Corpus/Repository/Workspace/Grants/Registry | Open — classify before move |

---

## D. Vlad decide (elevated fork list)

These are **product/architecture forks**. No refactor PR may assume an answer.

1. **Wave 0 / Turing-in-network** — kinds-as-data (one cell grain type) + Turing-completeness in the network, not the cell?  
2. **Destructive MCP** — RATIFIED remove destructive gate vs amended keep one-shot?  
3. **A18** — principal-on-delivery / per-principal stores vs owner-scoped inbox/stores (blocks honest two-people-one-brain)?  
4. **A19** — 15s fire wait vs OAuth/long ops?  
5. **Sharing wording** — deliberate install (no auto-propagate) vs “publish discoverable”?  
6. **Behavior / Library / Corpus** — Kernel-OS forever vs module? (**classify before any move**)  
7. **Workspace / Grants** — Kernel-OS forever? (**Vlad decide** — not “recommended keep”)  
8. **Outcome addressing** — keep tip journal-only on emitter, or restore inbox + caller / dual addressing + FixPath?  

Also decide (secondary): when fixed MCP principals die; Core `Microsoft.Extensions.AI` OK short-term?; docs trap-8 now vs after signoff?; after Chat/Time leave, is `DigitalBrain.Mcp` Kernel project or thin Sdk/AppHost host?; unify Abstractions Integration vs Sdk PrincipalTokenSlot?

---

## E. VerifiedActor placement (one-row truth)

| Concern | Owner | Tip path / rule |
|---|---|---|
| **Mint** (HTTP→ActorContext) | Kernel host | `Kernel/Auth/Surfaces/HttpActor.cs` (+ Identity cookie). Only trusted edge mints. |
| **Ambient** (RequestContext) | Core | `Core/Identity/VerifiedActor.cs` — `Current` / `Enter` API lives in Core. |
| **Re-enter** on drain/deliver | Core | `NeuronTurnCoordinator.DeliverAsync` re-enters from `SynapseDelivery.Principal`. |
| **MCP today** | **Spoof residual** | `DigitalBrain.Mcp/*Tools` `VerifiedActor.Enter` with `alice\|bob\|operator` — **illegal residual**; bypasses HttpActor. Local `fix/mcp-auth-principal` **unproven / not tip**. |

Modules/Sdk must not mint principals. Enter-at-trusted-edge only after mint is real (cookie/OAuth), never from tool string enums.

---

## 1. Vision spine

DigitalBrain = personal OS. Near-term: usable chat + voice + runtime UI. Differentiator: Turing-complete expressivity **in the network** (neurons/synapses/cells/graph as data). Work order: **Abstractions → Core → Kernel host** before Modules/Integrations cleanup. Stabilize-and-strangle unless Vlad ratifies a vision conflict. Folder `src/Kernel/**` ≠ ownership.

---

## 2. Layer table (target ownership)

### Abstractions — wire vocabulary only
| Keep | Notes |
|---|---|
| Synapse / RequestSynapse / SynapseDelivery (+ Principal) | Envelope |
| INeuron / IHandle / ISessionNeuron | Handler surface. **Tip truth:** `IHandle<T>` dispatches on directed delivery + manifests; **broadcast catalog enrollment = opt-in `[Broadcast]` only** (`Abstractions/Messaging/BroadcastAttribute.cs`). CLAUDE/UA “IHandle enrolls” is **wrong** — cite code. |
| SettledDeliveryFailure / NeuronAuthorizationException | Settled refuse vocabulary |
| Graph: ISynapseGraph, Connect/Disconnect (+ Connected/Disconnected), SynapseConnection, RouteOutcome + RouteOutcomeKind + Unrouted | Wire family permanent once data exists. Tip RouteOutcome = Reason-only (no FixPath). |
| Journals read/observe contracts | Audit |
| Identity ids + ActorContext | DTO only — no ambient |
| Capability descriptors/facts | Reflected, never handwritten |
| Cell apply/snapshot | If cells stay primitive — **Vlad decide** with Wave 0 |
| Workspace + Grants contracts | **Vlad decide** (open Q — not pre-endorsed) |
| OAuth path constants | Path string only; ports AppHost |

Wire aliases permanent once data exists: `db.*`, `ui.*`, `chat.*` / `probe.*`.

### Core — runtime interconnect only
Neuron*, Journal, Outbox, Turn, Pipeline, Concurrency, DeliveryPolicy; SynapseGraphNeuron; ConnectionRelay + transforms; Broadcast*; VerifiedActor ambient + drain re-enter; CapabilityInvocation + reification filters (`FrameworkInterfaces` = INeuron, ISessionNeuron, ISynapseGraph); DigitalBrainRuntime + ModuleAssemblies **hooks** (not product catalogs); PrincipalGraph/Registry/Grants **helpers**.

**Not Core:** chat UI, SF/Gmail product, `ui.*`, Conversation, ChatMessage freeze.

### Sdk — rails
IMcp* + list/call; OAuth/PKCE (McpAuthorization*, DurableMcpTokenCache, PrincipalTokenSlot); durable payload protection; WebhookIngress*. Stage 2: modules invent no parallel OAuth/webhook.

### Kernel host
Identity cookie + tables; HttpActor mint; PrincipalChat/Surface/Scoped; MapOAuthCallback; DigitalBrainHost; AppHost brain/kernel/mcp/scripting resources (UI **ports** AppHost); WorkspaceMembershipGateway. MapChat*/MapOwnerCommands/MapShellStreams OK short-term — do not pull `ui.*`/`chat.*` into Abstractions.

**Not host:** UI vocab, SF Contracts, AI/Memory/Time/Execution, Conversation.

### Modules
AI, UI (`ui.*`), Time, Memory, Google, Salesforce (**Contracts permanent**), Execution, Introspection. Authz via `VerifiedActor.Current` / `NeuronAuthorizationException` only.

---

## 3. Tip vs TARGET honesty (keep v1.1 rows)

| Area | Tip today | TARGET / debt |
|---|---|---|
| Outcome rail | Journaled into emitter incoming post-drain; not delivered | Plan had inbox+caller; no IInbox/FixPath |
| Refusal visibility | fire often timeout | Top open work |
| Broadcast (trap 8) | `[Broadcast]` opt-in | Docs wrong — cite code |
| Northbound identity | alice/bob/operator Enter in MCP | Spoof residual; fix branch unproven |
| Sdk↔Conversation | McpAuthorizationNeuron `chat`+`main` | Extract landmine |
| Core AI dep | ME.AI.Abstractions in Core.csproj | Don’t freeze ChatMessage |
| Auth project | Empty DigitalBrain.Auth/ | Live = Kernel/Auth/** |
| Host adapters | MapChat* etc. | Don’t pull ui/chat into Abstractions |
| Grey zone | Library/Behavior/Corpus/Repository | Classify before move |
| Dual catalog | AppHost vs ProductModules | §A.4 |
| HTML Brain/Cortex | Amended TARGET scenarios | Not tip verdicts; **no scores as honesty** |
| Destructive MCP / Sharing | Conflicts with RATIFIED | §D forks |

---

## 4. Hotspots (1–18)

1. `Kernel/DigitalBrain.Mcp/ChatTools.cs` → chat product in Kernel folder → UI/Conversations  
2. `Kernel/DigitalBrain.Mcp/TimeTools.cs` → Modules/Time  
3. `Kernel/DigitalBrain.Mcp/LibraryBehaviorTools.cs` → keep Enter-at-trusted-edge after real mint; move product tools out  
4. `Abstractions/Repository/*` / StartRepoReview → module contracts  
5. `Abstractions/Behavior/*` + `Core/Behavior/*` → **don’t relocate** until §D.6  
6. OAuthCallbackPaths + ProductSurfaceResources → path Abstractions/Sdk; ports AppHost  
7. Abstractions/Integrations vs Sdk PrincipalTokenSlot → unify?  
8. VerifiedActor + TurnCoordinator + SynapseDelivery.Principal → **protect** (§E)  
9. Kernel/Auth/** + HttpActor → **protect**  
10. Sdk/Mcp/** + Webhook/** → **protect**  
11. NeuronOutbox + StageIncomingOutcome → **protect**; refusal gap open  
12. Modules/UI `ui.*` → stay modular  
13. SF/Gmail McpServerDefinition + SalesForce/Contracts → keep Contracts  
14. Doc drift trap 8 → cite code; docs-only later  
15. ChatTools fixed principals → must die for principal honesty  
16. McpAuthorizationNeuron chat+main → Conversation landmine  
17. Core Microsoft.Extensions.AI → no ChatMessage freeze  
18. Empty DigitalBrain.Auth/ → don’t resurrect as Security package  

---

## 5. Outcome-rail footguns

- No await own Send mid-turn without FlushOutboxAsync.  
- Zero-receiver Emit = no outbox unless Unrouted staged.  
- Settled refuse → RouteOutcome on **emitter journal** (Reason-only; **no FixPath**).  
- Outcomes post-drain into emitter incoming; not delivered; join on **payload** Correlation.  
- Multi-hop: refusal on hop sender, not SystemTools.fire poller → timeout-blind after transforms.  
- Accidental broadcast: only `[Broadcast]` enrolls (not bare IHandle).  
- Non-RequestSynapse tools silently skipped.  
- Never emit mid-DrainAsync; recursion guard on outcome facts.  
- No IInbox / no dual addressing on tip.

---

## 6. Freeze checklist (no rename/move/weaken without Vlad)

- [ ] Wire aliases + graph family + shipped RouteOutcome/Unrouted shapes  
- [ ] journal-is-outbox; Emit vs Send; single-threaded turns  
- [ ] Streams ≠ interconnect  
- [ ] SystemTools trio; no team/convene/ask_llama creep  
- [ ] Salesforce Contracts boundary  
- [ ] Identity mint at HttpActor; modules never mint; MCP spoof must die before claiming principal rail  
- [ ] Protect VerifiedActor / HttpActor / Sdk OAuth+webhook / NeuronOutbox outcome staging  
- [ ] No ChatMessage / product chat types frozen into Core  
- [ ] Do not pull ui.*/chat.* into Abstractions via Map* “cleanup”  
- [ ] SOURCE→GREEN→GRILL→GATE→COMMIT; **no central test suite restore**  
- [ ] Grill veto §0 + CodeGraph §A + HTML map §B + Residuals §C  

---

## 7. Redesign / ratify before any refactor PR

All §D forks · single composition catalog · Conversation timing vs Core declutter · Behavior host design before Studio code · A18 vs MCP auth sequence · graph rename / project consolidation later · Core eviction only after classify.

---

## 8. Do not do yet

Folder/namespace cleanup PRs · modules ahead of Abstractions→Core · central tests · Streams interconnect · graph rename / catalog consolidation · Behavior Studio pre-design · parallel OAuth/webhook in modules · citing scenario scores as tip proof · claiming live MCP ratified.

---

## 9. Open questions (rolled into §D; kept for scan)

1. Behavior/Library/Corpus: OS vs module?  
2. Refusal visibility: kernel refuse-reply vs per-contract?  
3. DigitalBrain.Mcp after Chat/Time leave: Kernel vs thin host?  
4. Integration vs PrincipalTokenSlot unify?  
5. Workspace/Grants forever? (**Vlad decide**)  
6. Outcome addressing v2 / FixPath?  
7. When do alice/bob/operator die?  
8. Core ME.AI dep OK short-term?  
9. Docs trap-8 now?

---

## 10. Way of working

Architect owns this table. Eng Desk queues one seam **only after** Vlad signoff. Kernel Engineer: Core/host per brief. Product Grill: veto ship-theater. Integrations parked. Modules: Stage 2+. No production code from this draft.

**Next:** Product Grill re-grill v1.2 → CoS package → Vlad signoff/correct.
