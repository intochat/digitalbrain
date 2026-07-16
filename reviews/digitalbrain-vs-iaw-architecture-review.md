# DigitalBrain vs IAW — Architecture & Code-Quality Review

Read-only assessment. No code was modified during the review. Builds/tests were not run as part of the assessment (worktrees were clean; measurement came from static inspection).

**Primary repository:** `E:\brain`  
**DigitalBrain baseline commit:** `d3f7c4b3618c38d84de200594060d8c65c8c90db`  
**IAW repository:** `E:\IAW`  
**Review date:** 2026-07-16

---

## Worktree state (inspected first)

| | **DigitalBrain `E:\brain`** | **IAW `E:\IAW`** |
|--|--|--|
| Branch | `master` | `master` |
| HEAD | `2426fca554381aff43b427f06ed784d5924627ee` | `3aa3ba325553d9ad0403b750ccea92dcc5a0e2e0` |
| Upstream | `origin/master` @ same commit | `origin/master` @ same commit |
| Ahead/behind upstream | 0/0 | 0/0 |
| Staged / unstaged / untracked | **none** | **none** |
| vs baseline `d3f7c4b3` | **+1 commit**: Wave D dead-code delete (`2426fca5`) | n/a |
| Local intentional diffs | **None** — working tree matches HEAD | **None** — working tree matches HEAD |
| Remote | `digitalbraintech/brain` | `InteractiveAgents/IAW` |
| License | product repo | **MIT** |

**Implication:** All IAW findings below are about **committed upstream `master`**, not uncommitted local experiments. Docs under `E:\IAW\docs`, `website/`, and `docs/IAW-CLEANUP-PROMPT.md` are **stale relative to production code**.

---

## 1. Executive verdict

### Primary direction: **Retain and radically simplify DigitalBrain**

Secondary (selected only): **port a few IAW concepts**, not packages, not runtime replacement.

| Option | Verdict |
|--|--|
| 1. Keep + radically simplify DigitalBrain | **Primary GO** |
| 2. Port selected IAW concepts | **Yes, narrow list** (middleware shape, durable jobs idea, agent tool auto-discovery *for chat only*) |
| 3. Consume IAW packages directly | **NO-GO** |
| 4. Fork/adapt IAW as runtime | **NO-GO** |
| 5. Replace significant DigitalBrain runtime with IAW-inspired rewrite | **NO-GO as wholesale replace**; YES for deleting accidental layers *inside* DigitalBrain |

### Why not IAW as product runtime

1. IAW is a **developer coding-assistant / Telegram orchestration** stack, not a multi-tenant product with OAuth connectors, installable Features, grant fences, or Activity.
2. IAW **cannot productize arbitrary external MCP** (Gmail MCP etc.) today — only hardcoded Playwright/Aspire stdio clients.
3. IAW **script execution is unsandboxed `dotnet run`** with Shell/FileSystem/Git grain power — incompatible with DigitalBrain’s effect gate model without a full security rewrite.
4. DigitalBrain’s **Salesforce/Gmail effect rails, OAuth, Feature installation authority, and Activity** are real product safety surface; IAW Approver is LLM-judged tool gating with **in-memory waiters**.
5. Large parts of IAW’s published architecture (CheckpointStore, PersonalAssistant, TaskSupervisor, Memory agent suite) **do not exist in production code**.

### Why not “just keep all of DigitalBrain as-is”

Substantial accidental architecture remains: empty `INeuron`, dual “Ino” + “Feature” vocabularies, huge draft state machines, `AgentFrameworkWorkflowRunner` prompt heuristics, Legacy tests (~12.5k LOC / 57 files), contracts/integration project proliferation for two feature packs.

---

## 2. Architecture comparison

| DigitalBrain concept | IAW equivalent | Meaningful difference | Recommendation |
|--|--|--|--|
| **INeuron** (`IGrainWithStringKey` marker, only `IWebSearch` extends it) | `IAgent` + grain key | INeuron is **fake identity layer** | **Delete/collapse** INeuron; keep typed grains |
| **Feature** (`IFeature`, packs, install, release digest) | No product Feature; agents are static grains | Feature is product programmable unit with versioning | **Keep Feature as authoritative unit**; do not introduce Agent as parallel product unit |
| **capability** (`ICapabilityHandler`, grants, catalog) | AI tools on agents (`DefineTools` / interface discovery) | Capability = granted, versioned product ops; tools = LLM functions | **Keep capability for external effects**; simplify catalog |
| **integration** (Google/Salesforce/Web + OAuth connectors) | LLM API keys + Telegram bot token; no SaaS OAuth | DigitalBrain has real connection health + refresh | **Keep integrations**; do not replace with MCP stdio |
| **FeatureHost** (isolated worker, HTTP capability client, release ALC) | Agents.Host silo (everything in one silo) | FeatureHost isolates untrusted feature DLLs | **Keep** isolation boundary |
| **workflow runner** (`AgentFrameworkWorkflowRunner`) | `ThreadAgent` + optional `CodeOrchestratorAgent` | DB chat path: resolve capability → extract args → invoke Feature | **Simplify runner**; delete prompt-heuristic bloat after catalog is explicit |
| **approval / effect rail** (`IFeatureEffectApprovalGateway`, `IInoEffectPlan*`, Salesforce prepared payload) | `ApproverAgent` + `ToolApprovalMiddleware` | DB: prepared field diff, plan store, idempotent outcome; IAW: LLM allow/deny/ask + human buttons | **Keep DB rail**; port *only* the middleware choke-point idea if chat tools need a single gate |
| **Activity** (`ListActivity`, `FeatureRunSnapshot`, projection) | `AgentEvent` log / TaskLedger context lines | DB Activity is product run history with effect terminals | **Keep** |
| **BDD behavior** (Reqnroll feature packs + verification artifacts) | Generated C# orchestration scripts | BDD is verifiable install gate; scripts are free-form | **Keep BDD for Features**; reject scripts as Feature model |
| **persisted draft** (`FeatureHubState.Drafts`, ~1.8k-line authoring transitions) | Agent durable dict + chat history | DB drafts are product Studio lifecycle | **Keep, but compress transitions hard** |
| **IAW Agent** | — | Clean conversation+tools+state base | Port **shape** for chat assistant only; do not replace Features |
| **IAW tool** | capability / connector | Auto-registered interface methods | Useful for internal agents only |
| **IAW task** | Feature run / installation inbox | TaskLedger is append-only event list | Keep Feature runs; reject TaskLedger as product Activity |
| **durable job** (Orleans DurableJobs on Agent) | Feature schedules / reminders | Useful for recurrence | Port concept if Feature schedule path is weaker |
| **generated script** | Feature packs (compiled, verified) | Scripts are product-hostile without sandbox | **Reject for product** |
| **event/state model** | Feature hub/install state, Ino conversation ops | Both use Orleans; DB is more product-shaped | Keep DB; delete dual naming |

### Production path maps (verified)

**DigitalBrain chat → effect (product path)**

```
UiGrpc / MCP
  → FeatureAuthoringService | conversation
  → InoOperationWorkerGrain
  → AgentFrameworkWorkflowRunner
  → capability resolve/extract
  → FeatureCapabilityInvoker
  → FeatureInstallationGrain inbox
  → FeatureHost.FeatureExecutionWorker
  → HTTP capability execute on RuntimeHost
  → ICapabilityDispatcher
  → connector/effect
  → SalesforceFeatureEffectRail / GmailSendEffectHandler
  → approval UI
  → IInoEffectExecutor
```

**DigitalBrain Feature pack path**

```
Studio draft
  → build/publish artifact
  → FeatureHost loads release
  → HandleAsync on pack (e.g. EnrichSalesforceFeature)
  → ProposeAsync field update
  → human approval
  → apply
```

**IAW conversational path**

```
Telegram/DevUI/MCP
  → ThreadAgent
  → tools (incl. Execute → CodeOrchestrator)
  → Agent.ToolApprovalMiddleware
  → ApproverAgent
  → shell/roslyn/dotnet grains
```

**IAW MCP product path for external tools**

**Does not exist** for arbitrary servers. Only:

- `PlaywrightAgent.ConnectMcpAsync` → hardcoded `npx @playwright/mcp`
- `AspireAgent.ConnectMcpAsync` → hardcoded `aspire mcp start`
- `src/MCP` is an **MCP server** exposing `assistant_chat`, `agent_send_message`, etc. to **developers** (`.mcp.json` → `localhost:5300`)

---

## 3. Critical questions (answered from production code)

### 1. Can an IAW agent consume arbitrary external MCP servers at product runtime (e.g. Gmail MCP)?

**No.** Only Playwright and Aspire, hardcoded in agent activation. No Gmail MCP, no generic MCP registry.

### 2. Is `.mcp.json` only developer configuration?

**Yes.** IAW `.mcp.json` points Claude/IDE tools at local IAW MCP, Aspire, Context7, Playwright, etc. Runtime agents do **not** load `.mcp.json`. Same pattern for DigitalBrain `.mcp.json` (codegraph, aspire, context7, dart).

### 3. Exact path to expose Gmail MCP tools to a particular IAW agent?

**None in tree.** Would require new agent code: `McpClient.CreateAsync(...)` + `ListToolsAsync` + `DefineTools`, plus OAuth/token plumbing that does not exist. Not a config toggle.

### 4. Are MCP tools scoped by agent, user, tenant, conversation, and task?

**No product scoping.** Playwright/Aspire tools attach to that agent instance; Approver scopes by **user grain key prefix** and optional **thread id**, not tenant/MCP connection. MCP server `assistant_chat` uses grain `mcp/{threadSlug}` — no multi-tenant model.

### 5. How are OAuth connections, credentials, secrets, and token refresh represented?

- **IAW:** Aspire parameters for LLM/API keys; Telegram webhook secret. **No user OAuth connection store.**
- **DigitalBrain:** real Google OAuth state machine (`GoogleClientFactory` refresh_token keys, `IOAuthStateProtector`, `IConnector.CompleteAuthAsync`, Salesforce config seeder).

### 6. How does IAW prevent prompt-injected or unauthorized tool calls?

`Agent.ToolApprovalMiddleware` → `IApprover.Authorize` → LLM `JudgeAsync` allow/deny/ask → optional human resolve. Fail-closed if Approver errors. **Not** capability grants, **not** prepared effect payloads, **not** installation fences. Prompt injection can still influence Approver LLM judgment.

### 7. Can IAW require one exact human approval for a Salesforce field diff?

- **IAW: no.** Approver sees tool name + JSON args + recent messages — not a Salesforce prepared update envelope.
- **DigitalBrain: yes (product path).** `SalesforceFeatureEffectRail` + `SalesforceFeatureEffectPayload` (base64 prepared update + safe summary + expiry) + `IFeatureEffectApprovalGateway` + pending intents on installation grain. E2E tests cover idempotent propose/approve/decline.

### 8. Are tasks recoverable after process death?

**Partial.** Agent history/state/event log via Orleans Journaling (`DurableGrain`, `IDurableList`/`IDurableDictionary`). Scheduled jobs durable. **Approver waiters (`ConcurrentDictionary` `_waiters`) are in-memory** — in-flight human approval awaits die with process even if pending entry is written to state. Code orchestration is a **local process + temp workspace**, not a recoverable grain workflow. TaskLedger is durable **events**, not execution recovery.

### 9. Is CheckpointStore connected to the active execution path?

**No. Type does not exist** in `E:\IAW\src` (only mentioned in docs/plans). `ScriptExecutor` and `OrchestrationPlan` also **missing**.

### 10. How are generated C# scripts sandboxed, permissioned, compiled, timed out, audited, and cleaned up?

| Concern | Reality |
|--|--|
| Compiled | `dotnet build` in task dir |
| Timed | 10 min execution, 2 min build |
| Sanitized | `CodeValidator` (namespace/using cleanup only) |
| Sandboxed | **No** — full `dotnet run`, env mostly inherited, Orleans cluster client |
| Permissioned | Only Approver on orchestrator agent |
| Audited | Not first-class product audit trail |
| Cleanup | Ad hoc workspace under `IAW__Workspace` / temp |

### 11. Can IAW scripting express Given/When/Then without creating another competing Feature model?

Scripts can *do anything* via Shell/FileSystem — that **is** a competing unconstrained model. It does **not** produce installable, verifiable Feature packs with grants.

### 12. Is IAW Agent simpler and more coherent than DigitalBrain Feature + capability + neuron?

- **Yes for conversational tool agents.**
- **No as a product replacement** for installable automations + connectors + approval. DigitalBrain’s *product spine* is more coherent than its *names* (Ino/Feature/Neuron overlap).

### 13. Which DigitalBrain safety guarantees would be lost through IAW adoption?

- Feature installation authority digests / publication fences / grant revisions
- Prepared-effect + outcome-unknown semantics
- OAuth connection health gates on invoke
- FeatureHost process isolation for pack code
- Verification-before-install / artifact catalog
- Activity run projection with effect terminals
- Encrypted runtime state KEKs
- Internal FeatureHost token boundary

### 14. Which IAW mechanisms could directly replace DigitalBrain code?

- **Port concept:** single tool middleware gate; durable chat history pattern; agent interface→tool discovery for *assistant* only; durable jobs for scheduled work.
- **Not replace:** FeatureHost, effect rails, integrations, Studio authoring, Flutter Activity.

### 15. Is IAW itself overbuilt through excessive specialized agents?

**Yes.** ~14 LLM wrapper agents (`src/Agents/LLM/*`), coding swarm (Roslyn/DotNet/Git/Shell/NuGet/GitHub), orchestration (Thread, CodeOrchestrator, AgentSelector, TelegramUI), Approver, Validator, Explainability, Playwright, Aspire, IAWSystem. Docs still describe **deleted** PersonalAssistant / TaskSupervisor / Deployer / Planning / Reviewer / Memory agents.

### 16. Which repository has the smaller trustworthy production core after excluding generated code, tests, examples, and documentation?

| Metric (approx., static) | DigitalBrain | IAW |
|--|--|--|
| Production projects | ~18 (src+hosts+integrations+features+deploy) | ~11 packable/host projects |
| Prod C# LOC (excl. obj) | ~35k (src+hosts+integrations+features) | ~15k (Core+Agents+Agents.CSharp+other src) |
| Flutter prod UI | ~26k lines `app/lib` (excl. grpc gen) | DevUI Blazor + Telegram (smaller product UI) |
| Test LOC | ~32k + **~12.5k Legacy** | ~5.8k |
| Interface:class (rough) | 57 : 199 | 57 interfaces in Core alone : 126 classes src |
| Trustworthy *product* core | Feature install + effect + connectors + Host isolation | Agent + Approver + tools (coding) |

- **Smaller framework core:** IAW.
- **Smaller *trustworthy product automation* core:** DigitalBrain’s Feature+effect+connector path — *if* you delete accidental layers. IAW’s coding swarm is smaller but **not trustworthy for tenant SaaS mutations**.

---

## 4. Ranked findings

### P0 — Safety / product integrity

| ID | Repo | Evidence | Impact | Action |
|--|--|--|--|--|
| P0-1 | IAW | `CodeOrchestratorAgent.ExecuteProject` → `dotnet run` unsandboxed; Shell/FileSystem agents | Cannot host tenant Feature logic | **Reject** as DB runtime |
| P0-2 | IAW | No Gmail/Salesforce OAuth connectors; Approver LLM judgment only | Field-level Salesforce approval **impossible** without rebuild | **Reject** adoption for connectors |
| P0-3 | Brain | Live path must retain `SalesforceFeatureEffectRail` + `GmailSendEffectHandler` + plan store | Safety spine of product | **Retain** |
| P0-4 | IAW | `ApproverAgent._waiters` in-memory; process death drops waiters | Approval hangs not durable | If porting Approver idea, **rewrite** wait recovery |

### P1 — Accidental architecture / trust debt

| ID | Repo | Evidence | Impact | Action |
|--|--|--|--|--|
| P1-1 | Brain | `INeuron` empty; only `IWebSearch : INeuron` | Fake extensibility | **Delete** marker after renaming grains |
| P1-2 | Brain | Dual vocab: Ino* (`InoOperationWorkerGrain` ~1029 LOC) + Feature* | Cognitive load, dual rails | **Rename/consolidate** to one product language (Feature/Operation) |
| P1-3 | Brain | `FeatureDraftAuthoringTransitions.cs` ~1817 LOC; `FeatureHubTransitions` ~976 | State machine bloat | **Rewrite compress** after model delete-first |
| P1-4 | Brain | `AgentFrameworkWorkflowRunner` conversational heuristics + capability path | Two “assistants” in one class | **Simplify** to catalog resolve → extract → invoke |
| P1-5 | IAW | Docs claim CheckpointStore, PersonalAssistant, TaskSupervisor — **code missing**; ThreadAgent replaced PA | Docs lie; agents over-promise | Treat docs as **hypothesis only** |
| P1-6 | Brain | `tests/.../Legacy` 57 files / ~12.5k LOC | Slow gate, preserves dead shapes | **Delete** after caller proof that Orleans/E2E cover behavior |
| P1-7 | IAW | 14 LLM wrapper agents | Specialization tax | **Delete** for any DB port; use keyed `IChatClient` |

### P2 — Duplication / over-boundary

| ID | Repo | Evidence | Impact | Action |
|--|--|--|--|--|
| P2-1 | Brain | Integration + Contracts projects ×3 providers | Boundary tax for few handlers | **Retain contracts** for FeatureHost isolation; collapse if possible later |
| P2-2 | Brain | FeatureHost HTTP capability client + RuntimeHost dispatcher | Intentional isolation | **Retain** |
| P2-3 | Brain | One-impl interfaces (`IAgentWorkflowRunner`, `IFeatureRunGateway`, …) | Interface theater | **Inline** where no second impl planned |
| P2-4 | IAW | MCP server + Playwright MCP client + `.mcp.json` | Confusing MCP story | Document: **dev MCP ≠ product tools** |
| P2-5 | IAW | `TaskLedger` only Thread/Validator | Underused | Don’t port as Activity |
| P2-6 | Brain | `FeatureFanOutDeliveryRail` swallows exceptions → null status | Silent delivery failure | Fix when touching; don’t expand |

### P3 — Cleanup / quality

| ID | Repo | Evidence | Impact | Action |
|--|--|--|--|--|
| P3-1 | IAW | Swallowed catches in Telegram services | Noise | Clean if forking (not recommended) |
| P3-2 | IAW | Fire-and-forget `_ = LoadWorkspaceInBackground` in RoslynAgent | Recovery opacity | N/A for DB |
| P3-3 | Brain | Wave D already deleted telemetry/models (`remove.md` EXECUTED) | Good direction | Continue delete-first |
| P3-4 | IAW | `docs/IAW-CLEANUP-PROMPT.md` still orients around deleted types | Misleading | Ignore for architecture decisions |
| P3-5 | Brain | Flutter `Synapse*` names (kept false-dead) | Naming debt | Rename later (Wave E) |

---

## 5. Deletion proposal

### DigitalBrain — deletable soon (after caller proof / gate green)

| Target | Est. | Notes |
|--|--|--|
| `tests/DigitalBrain.OrleansTests/Legacy/**` | ~12.5k LOC, 57 files | Only if modern Feature/E2E/Orleans tests cover same contracts |
| Empty `INeuron` + Neuron naming leftovers | small | Collapse to grain contracts |
| Residual unused model descriptors / dead helpers | ongoing | Wave D pattern |
| Prompt-heuristic dead branches in workflow runner | medium | After explicit capability UX |
| Duplicate “Ino” type names that alias Feature/operation | medium rename | Not pure delete |

### DigitalBrain — after consolidation

| Target | Est. |
|--|--|
| Compress `FeatureDraftAuthoringTransitions` / hub transitions | target **>40%** LOC cut |
| Merge one-impl public interfaces into concrete types | small–medium |
| Possibly merge Web contracts into Kernel.Contracts if FeatureHost still isolated | project −1 |

### DigitalBrain — must remain for safety

- Feature installation grains + authority/grant/publication model
- Effect plan store + executor + outcome-unknown
- Provider connectors + OAuth
- FeatureHost isolation + internal token
- Feature verification/publication artifacts
- Activity / `FeatureRunSnapshot` projection
- Encrypted runtime state

### IAW trash / duplication (for awareness; do not import)

| Target | Status in worktree |
|--|--|
| CheckpointStore, ScriptExecutor, OrchestrationPlan, TaskSupervisor, Deployer, Planning, Reviewer, PersonalAssistant, Memory agent suite | **Absent** (docs/website still claim) |
| 14 LLM agents | Present — overbuilt |
| CodeOrchestrator `dotnet run` | Present — unsafe for multi-tenant product |
| Website/docs/superpowers | Large stale surface |

### Estimated reduction if DigitalBrain simplifies (not IAW merge)

- **LOC:** −15–25k (Legacy tests + authoring compression + runner/Ino rename cleanup)
- **Projects:** 0–2 removable without harming FeatureHost isolation
- **Conceptual models:** Feature / capability / connector / effect — **4**, not Agent+Feature+Neuron+Ino+Workflow+Tool

---

## 6. Target architecture (smallest coherent)

**Do not run DigitalBrain Features and IAW Agents as parallel product models.**

| Concern | Definition |
|--|--|
| **Authoritative programmable unit** | **Feature** (versioned pack + grants + release digest). Chat “assistant” is a **thin capability resolver**, not a second runtime. |
| **Identity & state** | `BrainOwnerId` / `ActorId` / `FeatureInstallationId` / run ids. Drop empty INeuron. |
| **Prompt & behavior** | Pack code (`IFeature.HandleAsync`) + optional LLM only for arg extraction / natural language — not free shell. |
| **Tool / MCP binding** | Product tools = **capabilities** bound to **OAuth connections**. Developer MCP (`.mcp.json`) stays out of tenant runtime. Optional later: curated MCP adapters that *emit* capabilities, never raw tools to the model. |
| **Execution** | FeatureHost worker + RuntimeHost capability/effect boundary (keep). |
| **Recovery** | Installation inbox + completions + effect plan durability (keep). No fire-and-forget approvals. |
| **Human approval** | Prepared effect + single decision id + idempotent apply (keep Salesforce/Gmail pattern). |
| **Events & Activity** | Project Feature runs + effect terminals to UI (keep). |
| **Authoring UI** | Flutter Studio + draft hub (keep; simplify state machine). |

IAW-inspired **only** where it shrinks chat path: one middleware gate for *read-only* tools, durable chat history if needed, fewer wrappers.

---

## 7. IAW adoption matrix

| Mechanism | Classification | Maintenance / coupling / migration |
|--|--|--|
| Agent base + journaling chat state | **Port concept** | Avoid package dep on alpha Orleans.Journaling/DurableJobs unless DB already on same stack |
| Tool auto-discovery | **Port concept** (assistant only) | Easy; don’t use for external effects |
| Approver LLM + human buttons | **Reimplement minimally** if chat tools need it | Do not replace prepared-effect rail |
| CodeOrchestrator / ScriptGenerator | **Reject** | Security + competing Feature model |
| Playwright/Aspire MCP clients | **Reject** for product | Dev-only pattern |
| IAW MCP server | **Reject** as product surface | Fine for internal agent ops later |
| LLM wrapper agents | **Reject** | Use one chat client config |
| TaskLedger | **Reject** | Activity already exists |
| Aspire.Hosting.IAW packages | **Reject** | Different product topology; MIT ok but coupling high |
| Memory provider | **Port concept only if** DB memory is weaker | DB already has memory capabilities |
| Testing (`AgentTest`) | **Reject** | DB has Orleans/E2E already |
| Docs/cleanup plans | **Reject** as requirements | Hypotheses, many false |

**Licensing:** IAW is MIT — legal fork is fine; **architecture/risk is not**.

---

## 8. Migration sequence (deletion-first, reversible)

1. **Checkpoint A — freeze decision** (this report). No IAW package reference.
2. **Delete Legacy test mass** after mapping each suite to a surviving test (or promoting one).
3. **Delete INeuron / dead markers**; keep `IWebSearch` as plain grain.
4. **Name consolidation plan**: Ino* → Operation/Feature vocabulary (rename-only PR).
5. **Compress draft authoring transitions** with behavioral tests green.
6. **Simplify AgentFrameworkWorkflowRunner** to explicit resolve→extract→invoke; drop conversational special cases that duplicate UI.
7. **Optional:** port durable-job pattern for Feature schedules *only if* measured gap.
8. **Never** add IAW CodeOrchestrator or dual Agent product model.

Each step: build + root tests + aspire doctor; reversible by git revert.

---

## 9. Code-quality measures (summary)

| Measure | DigitalBrain | IAW |
|--|--|--|
| Runtime models | Feature install loop + Ino conversation worker + FeatureHost poll | Agent GetResponse + optional code orch process |
| Persistence | Feature hub/install state, effect plans, encrypted blobs, OAuth packs | Journaling durable agent state, TaskLedger events |
| Registration | DI `ICapabilityHandler` + catalog projection | Interface tools + registry grain + hardcode MCP |
| Reflection/string dispatch | Capability ids (intentional); draft kinds | AgentInterfaceResolver, tool discovery, Approver LLM JSON |
| Unsafe execution | Pack code in isolated host; capabilities gated | **Shell + unsandboxed scripts** |
| Approval | Prepared effect + grants | LLM + human + memo policies |
| Tests | Heavy Orleans/E2E; Legacy mock-heavy residue | MockChatClient unit + “use live MCP” culture in CLAUDE |
| Observability | ActivitySources on Feature/Ino | AgentTelemetry + OTel gen_ai |
| Recovery | Strong on Feature/effect; conversation worker complex | Strong on agent state; weak on approval waiters / scripts |

### Key file / symbol anchors

**DigitalBrain**

| Symbol / path | Role |
|--|--|
| `src/DigitalBrain.Kernel.Contracts/Core/NeuronId.cs` — `INeuron` | Empty marker interface |
| `src/DigitalBrain.Kernel.Contracts/Runtime/WebSearchNeuron.cs` — `IWebSearch` | Only production `INeuron` implementor interface |
| `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs` | Chat capability resolve → invoke |
| `src/DigitalBrain.Kernel/Runtime/FeatureCapabilityInvoker.cs` | Connection health + run start + approval prepare |
| `src/DigitalBrain.Kernel/Runtime/CapabilityParameterModel.cs` | Server-selected capability arg extraction |
| `src/DigitalBrain.Mcp/FeatureAuthoringService.cs` | Draft/read/install authoring surface |
| `hosts/DigitalBrain.FeatureHost/*` | Isolated feature execution |
| `integrations/.../SalesforceFeatureEffectRail.cs` | Prepared update + human approval rail |
| `integrations/.../GmailCapabilityHandlers.cs` | Gmail effect handler |
| `src/DigitalBrain.Kernel/Features/FeatureRunProjection.cs` | Activity / run snapshots |
| `hosts/DigitalBrain.AppHost/AppHost.cs` | kernel, feature-host, mcp, flutter topology |
| `features/EnrichSalesforce/EnrichSalesforce.cs` | Product Feature pack example |
| `tests/DigitalBrain.OrleansTests/Legacy/**` | Historical test mass |

**IAW**

| Symbol / path | Role |
|--|--|
| `src/Core/Agents/Agent.cs` (+ partials) | DurableGrain agent base |
| `src/Core/Agents/Agent.Authorization.cs` | ToolApprovalMiddleware |
| `src/Core/Agents/Agent.Tools.cs` | Auto tool discovery |
| `src/Core/Contracts/IAgent.cs` | Agent contract |
| `src/Agents/Security/ApproverAgent.cs` | LLM + human approval (in-memory waiters) |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Generate + `dotnet run` scripts |
| `src/Core/Orchestration/ScriptGenerator.cs` | csproj generation only |
| `src/Agents/Web/PlaywrightAgent.cs` | Hardcoded Playwright MCP client |
| `src/Agents/Infrastructure/AspireAgent.cs` | Hardcoded Aspire MCP client |
| `src/MCP/Tools/AgentTools.cs` | Dev-facing MCP server tools |
| `src/Core/Grains/TaskLedgerGrain.cs` | Durable task event log |
| `docs/IAW-CLEANUP-PROMPT.md` | Stale hypotheses (CheckpointStore etc.) |

---

## 10. Hypothesis check (claimed IAW mechanisms vs code)

| Claim about IAW | Production truth on `E:\IAW` HEAD |
|--|--|
| Agent with prompts/specialized behavior | **Yes** (`Agent` partials, many agents) |
| Automatic/explicit tool registration | **Yes** (`Agent.Tools.cs`) |
| Task execution/scheduling | **Yes** durable jobs; TaskLedger events |
| Durable state/recovery | **Partial** (journaling yes; approval waiters/scripts no) |
| MCP connectivity | **Dev server + 2 hardcoded clients**, not product MCP marketplace |
| Generated C# scripting | **Yes**, unsandboxed |
| Orleans identity/comms | **Yes** |
| Events/streams/observability/memory | Events/streams/OTel **yes**; multi-memory agents **gone** (`IawMemoryProvider` only) |
| CheckpointStore / ScriptExecutor / OrchestrationPlan | **Missing** |
| PersonalAssistant / TaskSupervisor / Deployer / Planning / Reviewer | **Missing** (docs/website still claim; ThreadAgent replaced PA) |

---

## 11. Go / No-Go

| Decision | Call |
|--|--|
| Replace DigitalBrain runtime with IAW | **NO-GO** |
| Consume IAW NuGet packages | **NO-GO** |
| Fork IAW as product core | **NO-GO** |
| Keep DigitalBrain + delete accidental architecture | **GO** |
| Port narrow IAW ideas (middleware shape, chat durability, avoid LLM-agent zoo) | **GO (concepts only)** |

### First three actions (require explicit approval before implementation)

1. **Delete** `tests/DigitalBrain.OrleansTests/Legacy/**` (or quarantine behind a separate project) after confirming coverage map — largest pure waste.
2. **Delete** empty **INeuron** extensibility fiction; leave typed grains only.
3. **Compress** Feature draft authoring transitions + **rename** Ino/Feature dual language toward one model — without introducing Agents.

---

## Worktree rules honored

- Both repositories inspected; no reset/clean/checkout/switch/pull/fetch/merge/rebase/stash.
- No code modified during the original assessment.
- Local IAW changes: **none** (worktree clean and equal to upstream).
- Assessment of `E:\IAW` actual worktree, not a fresh clone.
