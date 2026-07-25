# MANDATORY: 200-agent DigitalBrain **test truth** campaign
# (exactly 200 · grill-first · zero magic strings · every test assessed · codegraph-led trash · vision-aligned)

You are the **orchestrator** (Grok / Claude / Codex) in the DigitalBrain monorepo.
Hard budget: **exactly 200 subagent cycles**. Cycle = one subagent, **one write scope**, one scoring
rule, one grill, one verify. Waves of **8–12** non-overlapping scopes. Zero user menus unless
irreversible — then recommend hard and proceed only if in-repo architecture already decides.

This campaign **supersedes** high-level “hosting theater / mega-file split” waves as the primary
mission. Prior campaigns left gates green and still **felt like a bad solution**: tests full of
string soup, dual product sentences, source-grep L0 pins that rot, and “proofs” that do not prove
the vision. Prefer **delete + simplify + named constants + one proof per concern** over new surface.

**Do not** spend cycles on vague “refactor architecture overview” or re-litigating Designed modules
(Behavior rail, calendar Time, supervised AI). **Do** make every remaining Built path feel inevitable
and every test read like a contract a human would keep.

---

## THE ONE VISION (non-negotiable — every agent restates in one sentence)

> **A brain you program by writing ordinary C#, and that can program itself.**
>
> **The OS UI is not a Flutter app with agents behind it.**
> **It is a brain whose UI vocabulary is a Flutter module, and whose logic
> is compositions/behaviors over that vocabulary.**
>
> **Northbound:** Flutter host → `hosts/DigitalBrain.Ui` (HTTP/SSE) → `IDigitalBrain` → silo journals.
> **Modules own vocabulary. Compositions own logic. Dart owns pixels only.**

### Fold conditions (delete or reverse)

| Temptation | Fold |
| --- | --- |
| Magic string for env key / resource name / route / alias duplicated in test | **Trash** → shared `const` / `nameof` / product constant |
| `File.ReadAllText` + `Assert.Contains("WithFlutterHost")` as primary proof | Source-grep theater — prefer runtime graph / type / API proof |
| Dual test helpers doing the same assertion with different string literals | Collapse to one fixture helper |
| Test that only re-states packages.md without a consumer fail mode | Zero-consumer trash |
| NSubstitute that mocks Kernel internals the product never exposes | Wrong seam |
| `[Fact(DisplayName = "...")]` that lies about behavior | Fix name or delete fact |
| Invent Behavior rail / IReminder / IFlutter god | Architecture regression |
| “Tests pass” without quoting command output | False claim |
| File **> 400 lines** without Explicit hold + residual entry | **Trash** |
| Headless presented as product Desktop | Lie (hosting is explicit: default Desktop, `HeadlessHost` only when chosen) |

---

## 0. Ground truth (every wave)

```
git rev-parse HEAD
git status --porcelain
git branch --show-current
```

If porcelain dirty and **you** did not dirty it: surface and stop that path.

**Baseline at prompt authoring (re-read live each wave):**

```
git rev-parse HEAD   # authoring tip a8eac6c1 on agent/digitalbrain-hosting-testing
```

Obey in order:

1. `CLAUDE.md` / `Claude.md` (gates, grilling, no narrative comments)
2. `docs/architecture.md` §§1, 3, **4.6 Flutter**, **5 Behaviors**, 6–9, **11**
3. `docs/packages.md`
4. `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md`
5. `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md` (must-not-return)
6. Historical scorecards are **not** proof of product quality

**Oracles:** compiler, test suite, git, **codegraph first**, then Context7 / Microsoft Learn /
dart MCP / Aspire MCP. Fall back loudly. **ALWAYS** codegraph before editing a symbol you did not
author in this cycle.

### Hard gates (never `--filter` for completion claims)

```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

When clients change:

```
$env:Path = "E:\tools\flutter\bin;" + $env:Path
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
# per package tests; shell/ flutter analyze + flutter test when chrome touched
```

Live product (only when hosting/Ui/host paths change — quote health):

```
aspire start --project hosts/DigitalBrain.AppHost   # after clean stop if needed
# silo + digitalbrain-ui + digitalbrain-flutter (Desktop default) + digitalbrain-mcp Healthy
# POST open-scene → SSE scene-opened
```

**Line-count gate:** any product/test `*.cs` / `*.dart` (exclude bin/obj/node_modules/.dart_tool/build/
platform embedders; `*.g.cs` if truly generated) **> 400 physical lines** = FAIL unless Explicit hold.

---

## 1. Scoring rule (copy into every subagent — exact)

Allowed only if ≥1:

1. **Architecture truth** — test or code lies about Flutter/hosting/compositions/modules vs vision
2. **Magic-string removal** — env keys, resource names, routes, aliases, package ids → named constants / `nameof` / product surface
3. **Test simplification** — fewer lines, one concern, clearer arrange/act/assert, delete redundant facts
4. **Trash delete** — zero-consumer helpers, dual paths, source-grep theater, husk fixtures
5. **Framework misuse** — xUnit / NSubstitute / Aspire / Orleans / .NET vs official docs (Context7 or compiler)
6. **Vision alignment** — northbound path; no second kernel; module owns surface composition
7. **Boundary honesty** — Kernel purity, packages.md, compositions never reference Kernel/runtimes
8. **Cohesion** — file ≤400; folder/namespace matches family; one proof family per file
9. **Live proof** — only when you touch hosting product sentence; quote aspire health

Forbidden: invent Behavior rail; widgets in C#; MCP as UI bus; OTel as UI truth; restore `app/`;
new public product API without red→green; “green unit tests” as substitute for Desktop host when
you claimed product OS surface.

### Magic-string policy (hard)

**Disallowed** in product and test code (except listed carve-outs):

- Duplicated env keys (`"DigitalBrain__Owner"`, `"DIGITALBRAIN_UI_BASE"`, …) — use product `const`s
- Duplicated resource names (`"digitalbrain-ui"`, `"silo"`, …) — use product `const`s or test fixture constants that **point at** product constants
- Duplicated HTTP paths (`"/shells/"`, `"scene-opened"`, …) — single edge contract constants
- Duplicated package/project path fragments in many tests — one `RepositoryLayout` / support type
- Wire aliases / synapse type names as raw strings in tests when reflection/`nameof`/golden already exists

**Carve-outs (must be justified in grill if expanded):**

- `[Fact(DisplayName = "...")]` human titles (no product protocol values)
- Regex pins against `docs/architecture.md` / `docs/packages.md` **only** in docs site tests
- Temporary directory names / Guid suffixes
- NSubstitute `Arg.Any` type args (not string protocol)
- Orleans `[Alias("...")]` on **product** types (already the wire contract — tests must not invent parallel aliases)

Campaign exit: **no new magic protocol strings**; net reduction of string-literal density in `tests/**`
measured by orchestrator scan (quote counts before/after).

---

## 2. Grill board (every agent before claiming done)

1. What has no consumer today?
2. What did I claim without a command?
3. What changed that I did not change?
4. Which **magic strings** did I remove vs leave (and why carve-out)?
5. Does this test prove a **product sentence** or only file shape?
6. Could this be runtime/API proof instead of source-grep?
7. Modules = vocabulary, compositions = logic, hosting = surface composition?
8. Avoid Kernel changes? (prefer yes)
9. Any file in scope > 400 lines?
10. Folders/namespaces honest after edit?
11. Did I delete more than I added when possible?
12. Would a new engineer understand the test without reading the implementation?
13. Live Aspire quoted if I touched hosting product sentence?

---

## 3. Each subagent prompt MUST include

1. Exact write scope (paths) — non-overlapping within the wave  
2. Architecture sections to obey  
3. Scoring rule (§1)  
4. Mission type: `assess-test` | `de-string` | `delete-trash` | `simplify-test` | `fixture-cohesion` | `product-const` | `live-aspire` | `docs-honesty`  
5. Codegraph query already run or required first action  
6. Verify commands (owning project first; root gate only at phase boundaries)  
7. Grill answers (13)  
8. Protected surfaces (§4)  
9. Must-not-return list  
10. Vision restatement (one sentence)  
11. **Autonomous mandate:** if trash or magic string found in scope, fix/delete in the same cycle — do not only report  
12. **No new public product API** unless red proof exists first  

---

## 4. Protected surfaces (surgical only; still must de-string if lying)

- Kernel **behavior** spine (may de-string / split; no product sentence rewrite without red→green)
- Generator public contracts + alias wire names
- Testing public API (`TestBrain`, AppHost fixtures)
- Built module public neuron contracts
- Flutter first-five contracts + dual golden
- Ui HTTP + SSE route shapes (constants OK; shape change needs red→green)
- Explicit Desktop/Headless hosting API (`WithFlutterHost` / `WithFlutterHost<HeadlessHost>`) once green

---

## 5. Codegraph mandate

**Before editing**, each agent runs `codegraph_explore` (or equivalent) with the **exact symbols and
files in write scope** and pastes findings into the return: callers, dual paths, stringy edges.

Orchestrator pre-scan (re-run every 2 waves):

| Priority trash cluster | Why |
| --- | --- |
| `tests/**/Hosting/*` + `FlutterHosting*` | High string density; source-grep pins; path soup |
| `tests/**/Boundary/*` + `PackageBoundarySupport` | Package graph strings; File.ReadAllText theater |
| `tests/**/Packages/*` especially `ResidualPackageGraphContracts` | ~180+ quote hits; inventory drift |
| `tests/DigitalBrain.Integrations.Tests/McpEdge.cs` | Fixture dump (~333 lines, ~200 quotes) |
| `tests/DigitalBrain.Ui.Tests/UiEdgeRoundTrip.cs` | Dual SSE parsers; path/route strings |
| `tests/DigitalBrain.Tasks.Tests/TaskLifecycle.cs` | Lifecycle dump; opaque strings |
| `tests/DigitalBrain.Time.Tests/*` | Recovery/lifecycle duplication |
| `tests/DigitalBrain.ModuleTests/OrchestrationL1.cs` | Orchestration stringy probes |
| `tests/DigitalBrain.Compositions.Tests/*` | Boundary + L1 mix; string scene keys |
| Product hosts/modules env/resource names | Constants must be **single source**; tests reference them |

---

## 6. Known inventory at authoring (75 test `.cs` files under `tests/`)

### High string-density / complexity (attack first)

| ~Quotes / lines | Path | Agent duty |
| --- | --- | --- |
| ~200 / 334 | `tests/DigitalBrain.Integrations.Tests/McpEdge.cs` | Split harness vs scenarios; constants; ≤400 |
| ~184 / 234 | `tests/DigitalBrain.Tests/Hosting/HostingProjectionContracts.cs` | De-string; runtime proof over source |
| ~180 / 221 | `tests/DigitalBrain.Tests/Packages/ResidualPackageGraphContracts.cs` | Shared inventory; delete dup |
| ~156 / 251 | `tests/DigitalBrain.Tests/Boundary/CompositionBoundaryContracts.cs` | Source-grep → real boundary |
| ~136 / 133 | `tests/DigitalBrain.Tests/Boundary/HostingPackageBoundaryContracts.cs` | Align with product host const |
| ~110 / 175 | `tests/DigitalBrain.Tests/Boundary/PackageBoundarySupport.cs` | Single layout root helper |
| ~108 / 213 | `tests/DigitalBrain.Tests/Hosting/FlutterHostingHostModeContracts.cs` | Use product constants only |
| ~102 / 213 | `tests/DigitalBrain.Tests/Flutter/FlutterContracts.cs` | Golden is oracle; no parallel strings |
| ~96 / 313 | `tests/DigitalBrain.Ui.Tests/UiEdgeRoundTrip.cs` | One SSE parser; route constants |
| ~92 / 291 | `tests/DigitalBrain.Tests/Boundary/AssemblyBoundaryContracts.cs` | De-string assembly names |
| ~88 / 231 | `tests/DigitalBrain.Tests/Hosting/FlutterHostingProjectionSupport.cs` | **The** hosting support; no forks |
| ~86 / 132 | `tests/DigitalBrain.ModuleTests/OrchestrationL1.cs` | Simplify; named vocab |
| ~66 / 136 | `tests/DigitalBrain.Compositions.Tests/ShellAndSurfaceCompositions.cs` | Scene keys as constants |
| ~58 / 318 | `tests/DigitalBrain.Tasks.Tests/TaskLifecycle.cs` | Split concerns; no magic |

### Every test project (assess **each** fact file)

| Project | Files to own (agents claim one file or tight pair) |
| --- | --- |
| `DigitalBrain.Tests` | Boundary/*, Hosting/*, Flutter/*, Packages/* |
| `DigitalBrain.Ui.Tests` | UiEdgeRoundTrip, UiHostComposition, LiveProductUiNorthbound, UiFixture |
| `DigitalBrain.Flutter.Tests` | ShellSceneRoundTrip, FlutterFixture |
| `DigitalBrain.Time.Tests` | CountdownLifecycle*, CountdownRecovery, ClientEntryPointCapability, TimeFixture |
| `DigitalBrain.Tasks.Tests` | TaskLifecycle, ScriptedWorker, TestVocabulary, TasksFixture, TasksHarnessModule |
| `DigitalBrain.Integrations.Tests` | McpEdge, SalesforceMutation, GmailReadMessage, AccountEnrichment*, Integrations* |
| `DigitalBrain.ModuleTests` | OrchestrationL1, OrchestrationProbes, AISmoke, ChatEdge, ModuleFixture |
| `DigitalBrain.Compositions.Tests` | ShellAndSurface*, CompositionBehaviorShape, CompositionChatEdge, CompositionsFixture |
| `DigitalBrain.TestingTests` | all *Contracts + fixtures/probes |
| `DigitalBrain.HostTests` | HostedBrain, FixtureExclusivity, AppHostFixtures |
| `DigitalBrain.Quickstart.Tests` | GreetingBehavior, QuickstartFixture |

**Assess template (paste into return for each file):**

```
File: …
Facts: N
Mission of this file in one sentence:
Proves product sentence? Y/N — which:
Magic strings found: [list] → action:
Source-grep theater? Y/N → action:
Redundant with: …
Delete candidates: …
Simplify plan: …
Verify: dotnet test <project> -c Release
```

---

## 7. Exactly 200 agent cycles — wave plan

### Wave T0 — Inventory + constant spine (agents 1–16)

| Agents | Scope | Mission |
| --- | --- | --- |
| 1–2 | codegraph + PowerShell string density scan | `assess-test`: publish residual table into campaign scorecard |
| 3–6 | Product constants spine: Flutter hosting env/resource names, Ui routes/SSE event names, MCP/health paths | `product-const`: single source; tests only consume |
| 7–10 | Shared test layout (`RepositoryLayout` / extend PackageBoundarySupport) | `fixture-cohesion`: one root locator |
| 11–14 | Kill duplicate support helpers across Hosting/Boundary | `delete-trash` |
| 15–16 | Scorecard + baseline quote counts | `docs-honesty` |

**Exit:** product constants exist for OS surface + Ui edge; density baseline quoted.

### Wave T1 — Hosting + Ui tests (agents 17–48)

One file (or support+one consumer) per agent. De-string, simplify, delete source-grep theater.

Priority: HostingProjectionContracts → FlutterHosting* → UiEdgeRoundTrip → UiHostComposition → LiveProductUiNorthbound → Selection contracts.

**Exit:** Hosting/Ui tests use product constants; dual SSE parsers gone or Explicit hold; hosting filter green.

### Wave T2 — Boundary + Packages L0 (agents 49–88)

Own each Boundary/* and Packages/* file. Prefer runtime/package graph APIs over `ReadAllText` where possible. ResidualPackageGraph + PackageBoundarySupport first.

**Exit:** Boundary suite green; magic package path strings centralized.

### Wave T3 — Module L1 suites (agents 89–128)

Time, Tasks, Flutter.Tests, ModuleTests, Integrations (McpEdge split mandatory).

**Exit:** each suite green; McpEdge ≤400 and structured; TaskLifecycle/Countdown not dumps.

### Wave T4 — Compositions + Quickstart + TestingTests + HostTests (agents 129–156)

Compositions prove Behavior-shaped files + journal L1 without Kernel. TestingTests stay Testing API consumers only. HostTests silo-without-OS-surface honesty.

### Wave T5 — Product code de-string alignment (agents 157–176)

Hosts (Ui, Mcp, AppHost, Host), Flutter.Aspire.Hosting, Client/Aspire — only where tests forced constants; no new surface. Ensure AppHost product sentence still `WithUiEdge` + `WithFlutterHost()` Desktop.

### Wave T6 — Docs honesty + site pins (agents 177–188)

architecture/packages/site.test.mjs align with explicit Desktop/Headless and test quality rules. No Built-live lies.

### Wave T7 — Full gates + density delta + scorecard (agents 189–200)

Root build/test, docs npm, dart/flutter if needed, live Aspire if hosting touched, line-count scan, **string-density before/after quote**, residual holds, hard stop at 200.

---

## 8. Orchestrator start now

1. Record HEAD/status/branch.  
2. codegraph + density scan → residual table.  
3. Spawn Wave T0 (agents 1–16).  
4. After each wave: re-read HEAD/status; density re-scan on touched trees; phase root gate when many tests moved.  
5. Prefer **delete + de-string + simplify** over new pin files.  
6. End at agent 200 with scorecard under `docs/superpowers/specs/` (durable close record — not a session checklist dump).

### Success is not

- “200 agents ran.”  
- “We added more Assert.Contains on source.”  
- “Gates green while tests are unreadable string soup.”  
- “Auto hosting lies again.”  
- “Overview refactor document with no file changes.”

### Success is

> **Every test file assessed. Magic protocol strings centralized or gone.**  
> **Trash duals deleted. Source-grep theater minimized.**  
> **xUnit / fixture / NSubstitute / Aspire / Orleans usage is boring and correct.**  
> **Tests and product read as the same vision: modules vocabulary, compositions logic, Ui edge northbound, Desktop host explicit.**  
> **Root gates green with quoted evidence.**  
> **Desktop product host still starts via `WithFlutterHost()` — not headless by accident.**

---

## 9. Subagent template (copy)

```
Wave T* agent K (mission: assess-test|de-string|delete-trash|simplify-test|fixture-cohesion|product-const|live-aspire|docs-honesty)

Vision restatement: <one sentence>

Write scope: <exact paths>
Obey: CLAUDE.md; architecture §4.6; packages.md; scoring rule §1 of prompt-200-test-truth.md
Codegraph first: <query>
Protected: <list>
Must-not-return: ProbeHost, UiGateway-in-Kernel, IFlutter god, Behavior theater, Auto hosting,
  Dart→Orleans, tokens in journals, wholesale app/, mega-files >400 without Explicit hold,
  new magic protocol strings

Actions:
1. codegraph + inventory magic strings / dual helpers / theater in scope
2. Assess each test file (template §6)
3. Fix/delete/de-string/simplify autonomously (net reduction preferred)
4. Prefer product constants + runtime proofs over source-grep
5. Verify: <dotnet test owning project; aspire if hosting product sentence>
6. Grill board §2 (13 answers)
7. Foreign dirty tree: leave unstaged

Do not expand scope. Do not invent Behavior rail. Do not claim green without output.
```

---

## 10. Verify commands (update as you prove)

```
# Density (orchestrator / agents — PowerShell)
# Count " in tests/**/*.cs excluding bin/obj; quote top offenders

dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build

# Hosting-touched:
aspire stop --apphost hosts/DigitalBrain.AppHost
aspire start --project hosts/DigitalBrain.AppHost
# expect Desktop flutter host under shell/ when WithFlutterHost() (not accidental headless)
```

---

## 11. Framework best-practice checklist (agents apply when editing tests)

### xUnit

- One concern per fact; `[Fact(DisplayName = "...")]` states the **contract**, not the implementation  
- Prefer `Assert.Throws` / `Assert.Single` / `Assert.Collection` over loops with loose strings  
- `IAsyncLifetime` fixtures: exclusive AppHost leases already exist — do not invent second locks  
- No `Thread.Sleep` — use controllable time / wait helpers already in Testing  

### NSubstitute (Integrations / ModuleTests)

- Substitute **edges** (MCP session, chat clients), not Kernel grains  
- `Received(1)` on the protocol method that product code calls — not private helpers  
- Reset scripts in fixture, not ad-hoc statics  

### Aspire

- L0 projection tests use `DistributedApplication.CreateBuilder` + resource graph assertions  
- Resource names/env keys from **product constants**  
- No second product sentence (hand-wired Ui when FlutterModule+With* exists)  

### Orleans / journals

- L1 uses `TestBrain` / fixtures — observe journals, do not reimplement journaling  
- Alias pins live in contracts + golden; tests must not invent parallel alias strings  

### .NET style (CLAUDE.md)

- No narrative `/// <summary>`  
- Names carry meaning  
- Latest packages only via `Directory.Packages.props` when versions change (rare this campaign)  

---

## 12. Residual scorecard file

Create/update (durable, not a task checklist spam):

`docs/superpowers/specs/2026-07-25-test-truth-scorecard.md`

Must include: HEAD baseline, cycle log, per-wave density numbers, Explicit holds, remaining
magic-string clusters, Desktop host live quote if re-proven.

---

**Hard stop at agent 200.**  
If density still high or theater remains, residual table is honest — do not invent agent 201.
