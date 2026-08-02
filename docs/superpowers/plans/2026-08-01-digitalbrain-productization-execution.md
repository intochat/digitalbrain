# DigitalBrain Productization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.  
> **Orchestration:** Multiple isolated **Grok CLI** sessions (one writer per worktree). **Codex** (or the session conductor) reviews diffs, runs independent gates, and never authors production code if following the prior Grok-orchestrated model.

**Goal:** Turn the working prototype into a dependable, deployable product that satisfies the nine acceptance criteria in `docs/superpowers/specs/2026-08-01-digitalbrain-productization-design.md` with real secrets, two product images, web deploy UX, and no placeholder theater.

**Architecture:** Keep neurons/synapses/Tasks/behaviors/memory/discovery. Fold northbound MCP into the brain service. Keep authored behavior code in a separate process. Exact catalog is authority; semantic projection is a hard retrieval gate. Dual live Google + Salesforce OAuth. Full Behavior Studio parity on web and desktop. Models via configurable endpoints (Ollama local, cloud on Azure). No v1 UI model marketplace.

**Tech Stack:** .NET 11, Orleans journaling, Aspire, MCP, Microsoft.Extensions.AI, Qdrant behind MemoryModule, xUnit v3, Flutter (window/headless/web), Docker Hub images `digitalbrain` + `digitalbrain-ui`, Azure Key Vault for secrets.

**Approved design:** `docs/superpowers/specs/2026-08-01-digitalbrain-productization-design.md`  
**Architecture parent:** `docs/superpowers/specs/2026-07-30-neurons-synapses-behaviors-design.md`

---

## Absolute gates (every session)

- **No implementation until the owner says execution is authorized** for this plan (or a named wave). Design + plan approval alone is not enough if the owner still requires a separate “go implement” message — treat the next owner message after plan review as that signal when they say so.
- Five Steps **in order** per slice: challenge requirements → delete → simplify → cycle time → automate last.
- TDD: failing proof first; never delete red proofs — mark Explicit if live-only.
- CodeGraph explore **before and after** structural edits; `codegraph sync .` when index lags.
- Context7 / microsoft-learn / official docs before library API use.
- Aspire MCP for resources/logs/traces (`aspire describe` if `list_resources` hangs). DigitalBrain MCP for journals/transcripts/live chat.
- **Never:** placeholders restored, weaker assertions, skipped tests to hide failures, secrets in git/journals/traces/vectors/prompts/manifests, `git reset --hard` / force-push without confirmation, authored assemblies in the silo process.
- Commit only at green boundaries; one logical change per commit; grill answers in message when relevant.
- Root gate before any “wave complete” claim:

```powershell
dotnet build DigitalBrain.slnx -c Release
# Prefer working runner; if MTP handshake fails, document and use per-assembly:
#   dotnet exec <project>/bin/Release/net11.0/<Assembly>.dll
dotnet test DigitalBrain.slnx -c Release
```

Flutter when `clients/` touched:

```powershell
cd clients/flutter/core  ; dart analyze ; dart test
cd clients/flutter/shell ; flutter analyze ; flutter test
# windows build when window host changed; web build when web host changed
```

### Standard Grok handoff (every writing session)

```text
lane:
base_sha:
head_sha:
commits:
codegraph_queries:
files_changed:
files_deleted:
trash_removed_and_proof:
tests_added:
red_command_and_failure:
green_commands_and_results:
build_command_and_result:
digitalbrain_mcp_evidence:
aspire_mcp_evidence:
remaining_risks:
scope_deviations:
```

---

## File / unit map (decomposition)

| Unit | Primary paths | Responsibility |
|---|---|---|
| Provider config honesty | `src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/McpHosting.cs`, Google/Salesforce hosting extensions, `McpOAuthOptions.cs`, AppHost | No fake defaults; real parameters; correct redirect |
| Northbound MCP fold | `os/DigitalBrain.OS.McpHost/*`, `os/DigitalBrain.OS.Host/Program.cs`, AppHost | MCP endpoints on brain service |
| Behavior worker packaging | `os/DigitalBrain.OS.BehaviorHost/*`, `src/core/behaviors/**`, AppHost/Docker | Isolated process; image layout I1a |
| AI discovery | `CapabilityRouter.cs`, projection, `Agent.cs`, Live tests | E1 NL retrieval + exact authority |
| BehaviorAuthor C1 | `src/modules/ai/.../BehaviorAuthor.cs`, Studio endpoints, Flutter | Real codegen ladder |
| Unions F1 | Behavior compiler/runtime, Studio/source samples | Multi-case input ship proof |
| Public memory G1 | `src/modules/memory/**`, sample consumer | Community-usable IVectorMemory |
| Web host K1 | Flutter shell web target, `digitalbrain-ui` static/serve | Deploy UX six-view parity |
| Live oracles | `os/tests/DigitalBrain.OS.Product.Tests/*` | Explicit real-secret proofs |
| Status honesty | `README.md` | Built vs Designed matches productization design |
| Image/deploy | Dockerfiles, publish pipeline notes, Key Vault mapping | Two Hub names; Azure secrets |

**Shared files (integrator only):** `DigitalBrain.slnx`, `Directory.Packages.props`, `os/DigitalBrain.OS.AppHost/AppHost.cs`, `README.md` (when multi-lane).

---

## Wave overview (Grok sessions)

| Wave | Lane ID | Goal | Depends on |
|---|---|---|---|
| 0 | `db-prod-0-preflight` | Ground, baselines, MTP diagnosis | none |
| 1 | `db-prod-1-honesty` | Delete OAuth placeholders; readiness; redirect; README status | 0 |
| 2 | `db-prod-2-mcp-fold` | MCP into digitalbrain host; AppHost simplification | 1 |
| 3 | `db-prod-3-worker-pack` | Behavior worker process packaging decision implemented | 1 |
| 4 | `db-prod-4-live-providers` | Explicit Gmail + Salesforce live gates with real secrets | 1 |
| 5 | `db-prod-5-semantic` | E1 NL discovery live/L1 proofs | 1, 4 partial OK |
| 6 | `db-prod-6-c1-author` | Replace BehaviorAuthor; admission wired | 1 |
| 7 | `db-prod-7-unions` | F1 multi-case ship proof | 6 |
| 8 | `db-prod-8-memory-public` | G1 sample + surface | 1 |
| 9 | `db-prod-9-web-k1` | Web host + six-view parity | 6 |
| 10 | `db-prod-10-legacy-delete` | ExecuteLegacy migrate/delete; DTO collapse; custody move | 3, 6, 9 |
| 11 | `db-prod-11-images` | Dockerfiles + local compose smoke; Key Vault config map | 2, 3, 9 |
| 12 | `db-prod-12-claim` | Full claim checklist; Codex arbiter | all |

Parallelism: after Wave 1 green, lanes **2, 3, 5 prep, 6, 8** may run in **separate worktrees** if file surfaces do not overlap. **4** needs secrets on owner machine. **9** needs Flutter ownership exclusive. Integrator merges in wave order.

---

## Wave 0 — Preflight (read-mostly)

### Task 0.1: Immutable ground

**Files:** read only

- [ ] **Step 1:** Record git state

```powershell
git rev-parse HEAD
git status --porcelain
git merge-base HEAD master
git log -5 --oneline
```

Expected: clean tree or fully accounted dirty files; note HEAD.

- [ ] **Step 2:** CodeGraph health

```powershell
codegraph sync .
codegraph status .
```

Expected: index up to date; record file/node/edge counts.

- [ ] **Step 3:** Build baseline

```powershell
dotnet build DigitalBrain.slnx -c Release
```

Expected: 0 errors.

- [ ] **Step 4:** Test runner diagnosis

```powershell
dotnet test DigitalBrain.slnx -c Release --nologo
# If exit 5 handshake:
dotnet exec src\modules\google\DigitalBrain.Modules.Google.Tests\bin\Release\net11.0\DigitalBrain.Modules.Google.Tests.dll
```

Expected: document whether MTP works; if not, open a **harness-only** follow-up (do not weaken product tests). Prefer fixing MTP in a small dedicated commit in Wave 0 if cheap; else standardize wave gates on `dotnet exec` per test assembly list until fixed.

- [ ] **Step 5:** Aspire snapshot (no restart)

```powershell
aspire ps
aspire describe
```

Expected: note health; do not use hanging `list_resources` MCP if known bad.

- [ ] **Step 6:** Handoff with baselines only — **no product code commit required** unless MTP fix is included.

---

## Wave 1 — Config honesty (D1)

### Task 1.1: Failing proof — run mode must not inject fake OAuth clients

**Files:**
- Test: `src/core/mcp/DigitalBrain.Integrations.Tests/` or new focused Aspire hosting test project under `src/core/mcp/` if hosting tests fit better — prefer extending existing hosting projection tests if present (`src/DigitalBrain.PublishGate.Tests/Hosting/` patterns).
- Modify: `src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/McpHosting.cs`

- [ ] **Step 1: Write failing test** that constructs or inspects run-mode parameter registration and asserts:
  - No parameter default equal to `local-dev`
  - No parameter default equal to `local-dev-secret`
  - No redirect default equal to `http://localhost/oauth/callback`
  - Authorization mode may remain explicitly opt-in for local loopback **only when** the operator sets it — not a silent fake client id

Example assertion shape (adapt to actual test host APIs used in repo):

```csharp
[Fact(DisplayName = "run-mode MCP provider parameters do not default to local-dev placeholders")]
public void RunMode_provider_parameters_have_no_placeholder_defaults()
{
    // Arrange: build the same parameter registration path McpHosting uses in IsRunMode
    // Assert: client id / secret / redirect defaults are absent or not placeholder strings
    Assert.DoesNotContain("local-dev", registeredClientIdDefault ?? "", StringComparison.Ordinal);
    Assert.DoesNotContain("local-dev-secret", registeredClientSecretDefault ?? "", StringComparison.Ordinal);
    Assert.DoesNotContain("http://localhost/oauth/callback", registeredRedirectDefault ?? "", StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run test — expect FAIL** while placeholders still exist in `McpHosting.cs` (~lines 88–110).

- [ ] **Step 3: Implement** — remove `localValue: "local-dev"` / `"local-dev-secret"` / `"http://localhost/oauth/callback"` from run-mode registration. Use `AddParameter(name, secret: …)` without fake defaults (same as non-run path). Keep descriptions that tell the operator what to set.

- [ ] **Step 4: Redirect guidance** — documentation/description must state the product callback path pattern: UI base URL + `/oauth/mcp/callback` (`FlutterHttpContract.McpOAuthCallbackPath`). If Aspire can bind redirect to the UI endpoint expression, do that; else require operator parameter with no dummy default.

- [ ] **Step 5: Provider readiness** — ensure missing ClientId/secret fails at OAuth options creation with clear module name (already in `McpOAuthOptions.Required`); add/adjust test that placeholder strings are rejected if you introduce an explicit “reject known placeholders” guard:

```csharp
// Optional harden: reject ClientId equal to "local-dev" even if user sets it
if (string.Equals(value, "local-dev", StringComparison.Ordinal)
    || string.Equals(value, "local-dev-secret", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"{server.DisplayName} is using a disallowed placeholder for '{key}'. Configure a real application credential.");
}
```

- [ ] **Step 6: Green tests** for Integrations + any hosting tests touched.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "fix: remove MCP OAuth placeholder defaults for honest product config"
```

### Task 1.2: README status honesty

**Files:**
- Modify: `README.md` Status table

- [ ] **Step 1:** Align Built/Designed with productization design: Memory Built; automatic discovery Built (lab) / product claim pending live E1; Behavior Studio surface Built; NL codegen Designed until C1 green; deploy images Designed until Wave 11; multi-model UI Designed (M3).
- [ ] **Step 2:** Remove or reword any implication that Behaviors product path is unused if AppHost still loads BehaviorsModule.
- [ ] **Step 3: Commit** `docs: align README status with productization design`

---

## Wave 2 — MCP fold (I1a)

### Task 2.1: Map and test current MCP surface

**Files:**
- Read: `os/DigitalBrain.OS.McpHost/*`, AppHost MCP project registration
- Test: `os/tests/DigitalBrain.OS.McpHost.Tests/*`

- [ ] **Step 1:** Inventory all MCP tools and routes (`/mcp`, OAuth well-known if any).
- [ ] **Step 2:** Write/adjust a composition test: “brain host exposes northbound MCP tools” (may start as AppHost expectation document in test).
- [ ] **Step 3: CodeGraph** `DigitalBrainMcpTools`, `McpHost`, AppHost `AddProject` MCP.

### Task 2.2: Host MCP on silo/brain process

**Files:**
- Modify: `os/DigitalBrain.OS.Host/Program.cs` (or dedicated extension)
- Modify: `os/DigitalBrain.OS.AppHost/AppHost.cs` — remove separate MCP project resource **or** keep process temporarily behind a feature flag only during migration; end state is **one** brain deployable exposing MCP
- Move/share tool types from `os/DigitalBrain.OS.McpHost/` into host-accessible library if needed (prefer keep tools in a class library referenced by Host)

- [ ] **Step 1:** Failing product/composition test that expects MCP endpoint on silo/brain base address (or documented brain MCP port).
- [ ] **Step 2:** Implement MapMcp / WithMcpServer on brain host; register same tools.
- [ ] **Step 3:** Point DigitalBrain MCP clients / `.mcp.json` URL at brain MCP endpoint for product.
- [ ] **Step 4:** Green McpHost tests (retarget) + smoke `list_active_neurons` via new URL.
- [ ] **Step 5:** Delete obsolete AppHost MCP project registration when tests green.
- [ ] **Step 6: Commit** `feat: fold northbound MCP into digitalbrain host`

**Do not** fold BehaviorHost worker into the silo process.

---

## Wave 3 — Behavior worker packaging (I1a)

### Task 3.1: Choose packaging (single decision in lane)

**Pick and implement exactly one:**

| Option | Implementation |
|---|---|
| **A (default)** | Single `digitalbrain` image entrypoint supervises silo + behavior-host child process; env for broker addresses |
| **B** | Compose/ACA: internal sidecar container not published as third Hub product name |

- [ ] **Step 1:** Document choice in commit message and a short comment only if entrypoint script needs it (no new docs tree files unless necessary).
- [ ] **Step 2:** Failing isolation test already exists (`AuthoredAssemblyIsolation`) — ensure it still proves silo does not load authored assemblies.
- [ ] **Step 3:** Entrypoint / Docker prep (full Dockerfile may wait Wave 11) — at minimum AppHost still runs separate BehaviorHost project process.
- [ ] **Step 4: Commit** `feat: pin behavior worker process boundary for product packaging`

---

## Wave 4 — Live dual providers (B2, D1)

**Prerequisite:** Owner has real Google + Salesforce app credentials in Aspire parameters / user-secrets / Key Vault mapping. **Mocks only in non-Explicit tests.**

### Task 4.1: Gmail live oracle

**Files:**
- Modify/extend: `os/tests/DigitalBrain.OS.Product.Tests/LiveAutomaticGmail.cs` (or equivalent)
- Support: `LiveProductAspire.cs`

- [ ] **Step 1:** Explicit test asserts:
  - Semantic/exact path yields Gmail capability tool (or journal shows tool selection)
  - `GmailRequest` path / provider activity
  - If unauthorized: user-action visible; after auth, **same Task/command** continues
  - No secrets in journals
- [ ] **Step 2:** Run with real secrets:

```powershell
dotnet test os/tests/DigitalBrain.OS.Product.Tests -c Release -- -explicit only
```

- [ ] **Step 3:** Quote DigitalBrain MCP journals + Aspire traces in handoff.
- [ ] **Step 4: Commit** only test/product fixes: `test: live Gmail product oracle for productization`

### Task 4.2: Salesforce live oracle

- [ ] **Step 1:** Explicit live test for read + approval-gated mutation path with real SF app.
- [ ] **Step 2:** Same secret/journal rules as Gmail.
- [ ] **Step 3: Commit** `test: live Salesforce product oracle for productization`

---

## Wave 5 — Semantic discovery (E1)

### Task 5.1: Strengthen unit proofs

**Files:**
- `src/modules/ai/DigitalBrain.Modules.AI.Tests/AutomaticCapabilityDiscovery.cs`
- `src/modules/memory/DigitalBrain.Modules.Memory.Tests/CapabilityProjection.cs`
- `src/modules/ai/DigitalBrain.Modules.AI/Capabilities/CapabilityRouter.cs`

- [ ] **Step 1:** Add/extend tests that **fail** if only exact-term fallback would pass but semantic path is broken (inject search that must be hit).
- [ ] **Step 2:** Ensure poison/stale vector metadata never validates.
- [ ] **Step 3: Commit** `test: require semantic path for capability discovery gate`

### Task 5.2: Live NL tool selection

- [ ] **Step 1:** Explicit or DigitalBrain MCP scripted turn: “read my last three emails” with real config; assert tool/catalog evidence, not merely assistant prose.
- [ ] **Step 2:** Handoff journals. Commit if code changes: `fix: improve capability projection/retrieval for NL Gmail discovery`

---

## Wave 6 — C1 BehaviorAuthor

### Task 6.1: Kill theater — failing behavioral tests

**Files:**
- `src/modules/ai/DigitalBrain.Modules.AI/BehaviorAuthoring/BehaviorAuthor.cs`
- Tests: `src/modules/ai/DigitalBrain.Modules.AI.Tests/BehaviorAuthoring.cs`
- UI: `os/DigitalBrain.OS.Ui/BehaviorEndpoints.cs`, Flutter `behavior_view_model.dart`, `behavior_client.dart`

- [ ] **Step 1: Write failing test** — given a change request and current program, `ApplyApprovedScenarios` (or successor API) must produce **different** program source that compiles and binds scenarios when a scripted chat client returns a known program (use `ScriptedChatClient` / test edge — **not** live model in L1).

```csharp
[Fact(DisplayName = "approved scenario change materializes new C# from model output, not the unchanged stub program")]
public async Task Approved_change_emits_model_program_not_passthrough()
{
    // Script model to return a specific IBehaviorProgram source
    // Assert result.ProgramSource contains expected type name and differs from request.CurrentProgramSource
}
```

- [ ] **Step 2: Implement** BehaviorAuthor (or rename) using `IChatClient` / agent path with strict prompt: emit single-file behavior only; parse fenced C#; run existing compiler + BDD gate before returning ReadyForPropose.
- [ ] **Step 3: Wire Flutter shell** to call `runTests` / `approve` / `activate` APIs already on `BehaviorClient` after propose.
- [ ] **Step 4: L1 green** with scripted model.
- [ ] **Step 5: Explicit live C1** with real model later in Wave 12.
- [ ] **Step 6: Commit** `feat: real behavior codegen path for C1 studio ladder`

---

## Wave 7 — Unions (F1)

### Task 7.1: Ship proof multi-case input

**Files:**
- Compiler: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorInputContractCompiler.cs`
- Tests: `InputUnionCompilation.cs` and a product/BDD or Behaviors test that publishes a multi-case behavior

- [ ] **Step 1:** Failing acceptance-style test: behavior with root union of two cases publishes and dispatches by case without central interface edit.
- [ ] **Step 2:** Implement gaps only if test fails on current compiler.
- [ ] **Step 3:** Studio/source can display union cases (web+desktop after Wave 9 if needed).
- [ ] **Step 4: Commit** `test: multi-case behavior input union product proof` (+ feat if code)

---

## Wave 8 — Public IVectorMemory (G1)

### Task 8.1: Community-style sample

**Files:**
- Sample under `samples/` or thin module using only public Memory contracts
- Tests: Memory module tests + sample composition test

- [ ] **Step 1:** Failing composition test: sample stores/searches in non-reserved namespace; cannot write reserved capability namespace.
- [ ] **Step 2:** Implement sample; ensure no public Qdrant types in sample.
- [ ] **Step 3: Commit** `feat: public IVectorMemory sample consumer for G1`

---

## Wave 9 — Web host + K1 parity

### Task 9.1: Web host target

**Files:**
- `src/modules/flutter/**` hosting extensions (window/headless/web)
- `clients/flutter/shell/` — web entry
- `os/DigitalBrain.OS.Ui` — serve or proxy web assets if required

- [ ] **Step 1:** Add Flutter web build to product path; AppHost/dev may still use window; deploy path uses web.
- [ ] **Step 2:** Failing widget/integration checklist for six views on web (may start as Flutter tests + manual Explicit).
- [ ] **Step 3:** Implement shared view-model already in shell; fix web-only gaps (auth redirect, SSE, browser open).
- [ ] **Step 4:**

```powershell
cd clients/flutter/shell
flutter analyze
flutter test
flutter build web
```

- [ ] **Step 5: Commit** `feat: flutter web host for deploy Behavior Studio parity`

### Task 9.2: Six-view parity verification

- [ ] Library, Overview, Scenarios, Assistant change, Source, Revisions — automated where possible; remaining Explicit checklist for Wave 12.
- [ ] Commit test additions: `test: behavior studio web parity coverage`

---

## Wave 10 — Legacy delete / simplify

### Task 10.1: Migrate run-once to Task rail

**Files:**
- `BehaviorNeuron` ExecuteLegacy path
- `SynapseCapabilityTool.MaterializeBehavior`
- `BehaviorEndpoints` run-once
- Broker clients

- [ ] **Step 1:** CodeGraph all `ExecuteLegacy` callers.
- [ ] **Step 2:** Failing tests for run-once via Task+worker only.
- [ ] **Step 3:** Migrate callers; delete legacy transport when unreferenced.
- [ ] **Step 4: Commit** `refactor: remove legacy behavior execute path`

### Task 10.2: Collapse dual phase enums / wire DTOs

- [ ] Shared contracts for broker wire types; single phase enum ownership in Tasks.Contracts.
- [ ] Commit `refactor: unify behavior task operation wire contracts`

### Task 10.3: Move MemoryUserActionCustody

- [ ] Out of packable Behaviors SDK into Testing or test assembly.
- [ ] Commit `refactor: move test custody off packable behaviors SDK`

---

## Wave 11 — Images and Azure secret mapping

### Task 11.1: Dockerfiles

**Files:**
- Create: `os/DigitalBrain.OS.Host/Dockerfile` (or repo-standard location) for `digitalbrain`
- Create: `os/DigitalBrain.OS.Ui/Dockerfile` for `digitalbrain-ui`
- Optional compose for local smoke with external Qdrant/Ollama

- [ ] **Step 1:** Images contain **no** secrets.
- [ ] **Step 2:** Entrypoint respects I1a worker process choice from Wave 3.
- [ ] **Step 3:** Document env vars for Key Vault-injected: Google client id/secret/redirect, Salesforce client id/redirect, model endpoint/key, broker credential, storage, Qdrant.
- [ ] **Step 4:** Local `docker build` smoke (no push unless owner authorizes).
- [ ] **Step 5: Commit** `feat: product Dockerfiles for digitalbrain and digitalbrain-ui`

**Do not** `docker push` or Azure deploy without explicit owner authorization.

---

## Wave 12 — Product claim

### Task 12.1: Checklist run (owner secrets)

Execute design §8 claim checklist; paste command outputs and MCP/journal quotes into handoff.

- [ ] Build + full test gate green  
- [ ] No OAuth placeholders in running config  
- [ ] Live Gmail 2–3  
- [ ] Live Salesforce  
- [ ] E1 NL discovery evidence  
- [ ] C1 ladder on **web**  
- [ ] F1 union proof  
- [ ] G1 sample proof  
- [ ] Isolation proof  
- [ ] Secret redaction spot-check  

### Task 12.2: Codex / independent arbiter

- [ ] Read-only review of merge-base..HEAD productization delta against design.  
- [ ] Reject scope drift and theater.  

### Task 12.3: Finish branch (only when owner asks)

Use `superpowers:finishing-a-development-branch` **after** claim green:

1. Merge back to master locally  
2. Push and create a Pull Request  
3. Keep the branch as-is  

---

## Spec coverage matrix (plan ↔ design)

| Design section | Wave/Task |
|---|---|
| D1 secrets / no placeholders | 1 |
| README honesty | 1.2 |
| I1a MCP fold | 2 |
| I1a behavior process | 3, 11 |
| B2 dual live providers | 4 |
| E1 semantic | 5 |
| C1 BehaviorAuthor | 6 |
| F1 unions | 7 |
| G1 public memory | 8 |
| J2+/K1 web parity | 9 |
| Legacy delete / simplify | 10 |
| Docker + Key Vault map | 11 |
| Nine criteria claim | 12 |
| M3 / multi-model later | Explicit non-goal — no wave |
| L2 model endpoints | 11 env map; hosting already partial |

---

## Plan self-review

| Check | Result |
|---|---|
| Spec coverage | All ship gates mapped to waves |
| Placeholders | No TBD tasks; Wave 3 forces A/B packaging pick |
| TDD | Each feature wave starts with failing proof |
| Isolation | Repeated non-negotiable: no silo load of authored code |
| Secrets | Push/deploy forbidden without owner; tests mock by default |
| Parallelism | File-surface guidance for Grok worktrees |

---

## Execution handoff

**Plan complete and saved to** `docs/superpowers/plans/2026-08-01-digitalbrain-productization-execution.md`.

**Implementation is still not started.**

When you want code changes, reply with explicit authorization, for example:

- `Authorize execution of Wave 0–1`  
- or `Authorize full plan execution`  

**Execution options (after authorize):**

1. **Subagent-Driven (recommended)** — fresh agent per task/wave, review between waves  
2. **Isolated Grok CLI worktrees** — as in the original Grok-orchestrated model (one writer per worktree)  
3. **Inline in this session** — executing-plans style, checkpoint after each wave  

**Which approach, and do you authorize execution now or only after you review the plan file?**
