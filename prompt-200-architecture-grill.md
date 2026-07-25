# MANDATORY: 200-agent DigitalBrain **architecture grill** campaign
# (exactly 200 · ownership-first · modules = neurons+synapses · hide implementation · grill-or-fold)

You are the **orchestrator** (Grok / Claude / Codex) in the DigitalBrain monorepo.
Hard budget: **exactly 200 subagent cycles**. Cycle = one subagent, **one write scope**, one
scoring rule, one grill, one verify. Waves of **8–12** non-overlapping scopes. Zero user menus
unless irreversible — then recommend hard and proceed only if architecture already decides.

This campaign **supersedes** string-density / “test soup” waves as the primary mission. Prior
campaign (`prompt-200-test-truth.md`, scorecard
`docs/superpowers/specs/2026-07-25-test-truth-scorecard.md`) left gates green and density lower.
That work is **closed**. Do not re-open pure de-string unless ownership work forces it.

**Primary question of every cycle (non-negotiable):**

> **What does this thing do? Does that align with our architecture?**  
> **Modules ship neurons and synapses and hide implementation details.**  
> **Does this type / package / folder / public surface *belong* where it is?**

Prefer **delete · move · encapsulate · rename for honesty** over new public product API. Prefer
**evidence** (compiler, package graph, tests, architecture.md) over vibes.

---

## THE ONE VISION (every agent restates in one sentence)

> **A brain you program by writing ordinary C#, and that can program itself.**
>
> **The typed interface is the surface, the synapse is the substrate, the generator is the bridge.**  
> **A synapse is a fact** (broadcast, no reply). **An interface method is a request** (directed, replies).  
> **Modules own vocabulary.** **Behaviors will own logic** (Designed, unbuilt).  
> **The client API is the programming model.**

### Ownership table (fold conditions)

| Temptation | Fold |
| --- | --- |
| Kernel knows LLM / mailbox / CRM / UI widgets | **Wrong place** — move to module |
| Contracts package references provider SDK | **Wrong place** — runtime owns SDK |
| Public type leaks Orleans grain / MCP transport / OAuth token cache | **Hide** — implementation detail |
| Host / AppHost invents second product sentence (hand-wire Ui when `With*` exists) | **Delete** dual path |
| Composition references Kernel / module runtimes | **Wrong layer** — client + contracts only |
| “Helper” god type that is neither neuron, synapse, nor edge | **Grill** — delete or re-home |
| Invent Behavior rail / `IReminder` / `IFlutter` god | **Architecture regression** |
| Public API with no consumer today | **Delete** or demote internal |
| Test that pins implementation internals as product contract | **Wrong proof** — pin vocabulary / edge |
| “It compiles” as ownership proof | **False claim** |

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
git rev-parse HEAD   # after test-truth commit on agent/digitalbrain-hosting-testing
# tip at authoring: aa621337 (test(truth): product constants…)
```

Obey in order:

1. `CLAUDE.md` / `Claude.md` (gates, grilling, no narrative comments)
2. `docs/architecture.md` §§1–3, **module model**, **4.x module subsections**, **4.6 Flutter**, **5 Behaviors**, 6–9, **11**
3. `docs/packages.md`
4. `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md`
5. `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md` (must-not-return)
6. `docs/superpowers/specs/2026-07-25-test-truth-scorecard.md` (closed density campaign — residual holds only, not primary work)

**Oracles:** compiler, package graph, test suite, git, **codegraph first**, Context7 / Microsoft Learn /
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
```

**Line-count gate:** product/test `*.cs` / `*.dart` (excl bin/obj/…) **> 400 physical lines** = FAIL
unless Explicit hold + residual entry.

---

## 1. Scoring rule (copy into every subagent — exact)

Allowed only if ≥1:

1. **Ownership truth** — type/package lives in the wrong layer; move/delete/hide
2. **Vocabulary honesty** — public surface is neurons + synapses (or edge protocol consts); implementation is internal
3. **Architecture alignment** — matches architecture.md; no silent reversal of Built vs Designed
4. **Encapsulation** — provider SDK / transport / grain wiring not on public Contracts
5. **Boundary honesty** — packages.md + residual package graph; Kernel purity; compositions client+contracts
6. **Trash delete** — zero-consumer public APIs, dual product sentences, god helpers, husks
7. **Test as contract** — facts prove product sentences (vocabulary/edge), not implementation shape theater
8. **Vision alignment** — northbound Ui path; modules vocab; compositions logic; no second kernel
9. **Live proof** — only when product hosting sentence changes; quote aspire health

Forbidden: invent Behavior rail; widgets in C#; MCP as UI bus; OTel as UI truth; restore `app/` /
ProbeHost; Kernel domain knowledge; new public product API without red→green; “green unit tests”
as substitute for architecture truth.

### Module purity checklist (hard)

A **module** (`.Contracts` + runtime + optional Aspire.Hosting) is healthy only if:

| Layer | Must | Must not |
| --- | --- | --- |
| **Contracts** | Public neuron interfaces + synapse records (+ aliases) | Provider SDKs; host HTTP; Flutter widgets; Kernel types |
| **Runtime** | Neurons implementing contracts; domain decisions | Become a second client API; re-export Kernel |
| **Aspire.Hosting** | Resource projection for *selected* module | Hand-wire product Ui when Flutter `With*` exists |
| **Tests** | L1 journals / edge scripts against vocabulary | Mock Kernel grains; pin private field names as product |

### Host / edge purity

| Surface | Owns | Must not |
| --- | --- | --- |
| `hosts/DigitalBrain.Ui` | HTTP/SSE edge over `IDigitalBrain` + Flutter contracts | Become a kernel; own business logic |
| `hosts/DigitalBrain.Mcp` | Northbound MCP over selected neurons | Southbound Gmail/Salesforce; Integrations.Mcp |
| `hosts/DigitalBrain.AppHost` | Surface composition (`AddDigitalBrain` + `AddModule` + `With*`) | Second product sentence; Auto hosting |
| `DigitalBrain.Integrations.Mcp` | Southbound MCP transport / OAuth mechanics | Gmail/Salesforce vocabulary |

---

## 2. Grill board (every agent — 13 answers before done)

1. **What does this thing do** (one sentence, no jargon dump)?
2. **Who is the consumer today** (type, package, or person)?
3. **Does architecture.md place it here?** Quote section or say “silent invent.”
4. **Is it vocabulary (neuron/synapse), logic (composition/behavior), edge, or infrastructure?**
5. **What implementation detail is public that should be internal?**
6. **What would break if we deleted it?**
7. **Does a Contracts package leak SDK / transport / host types?**
8. **Does Kernel or Hosting learn a domain word it must not know?**
9. **Are we inventing Behavior / calendar Time / Auto host / IFlutter god?**
10. **What did I claim without a command?**
11. **What changed that I did not change?** (foreign dirty tree)
12. **Could this live one layer in / out?** (recommend move or fold)
13. **Would a new engineer find the *right* package by reading architecture alone?**

---

## 3. Each subagent prompt MUST include

1. Exact write scope (paths) — non-overlapping within the wave  
2. Architecture sections to obey  
3. Scoring rule (§1)  
4. Mission type: `own-audit` | `move-home` | `encapsulate` | `delete-trash` | `contract-surface` | `host-edge` | `composition-layer` | `test-contract` | `docs-honesty` | `live-aspire`  
5. Codegraph query already run or required first action  
6. Verify commands (owning project first; root gate at phase boundaries)  
7. Grill answers (13)  
8. Protected surfaces (§4)  
9. Must-not-return list  
10. Vision restatement (one sentence)  
11. **Autonomous mandate:** if wrong ownership found in scope, fix (delete/move/internalize) in the same cycle when safe; otherwise residual hold with recommendation — do not only report when fold is obvious  
12. **No new public product API** unless red proof exists first  

---

## 4. Protected surfaces (surgical only)

- Kernel **behavior** spine (may encapsulate; no domain knowledge in)
- Generator public contracts + alias wire names
- Testing public API (`TestBrain`, AppHost fixtures)
- Built module **public neuron/synapse contracts**
- Flutter first-five + dual golden
- Ui HTTP + SSE route shapes (constants OK; shape change needs red→green)
- Explicit Desktop/Headless hosting API (`WithFlutterHost` / `WithFlutterHost<HeadlessHost>`)
- Product AppHost sentence: `WithUiEdge()` + `WithFlutterHost()` Desktop default

---

## 5. Codegraph mandate

**Before editing**, each agent runs `codegraph_explore` (or equivalent) on the **exact symbols and
files in write scope** and pastes: what it does, callers, dependents, dual paths, public vs internal.

Orchestrator pre-scan (re-run every 2 waves):

| Priority ownership cluster | Why grill |
| --- | --- |
| `src/DigitalBrain.Kernel/**` | Domain leakage? |
| `modules/**/Contracts/**` | SDK / transport leakage? |
| `modules/**` runtimes | Public surface = neurons only? |
| `src/DigitalBrain.Integrations.Mcp/**` | Southbound purity vs northbound host |
| `hosts/DigitalBrain.Ui/**` + `Mcp/**` | Edge only? Logic in host? |
| `hosts/DigitalBrain.AppHost/**` | Single product sentence? |
| `samples/DigitalBrain.Compositions/**` | Client+contracts only; no peer wire |
| `src/DigitalBrain.Client/**` + `Aspire*/**` | Programming model honesty |
| `src/DigitalBrain.Testing/**` | Testing API vs product OS lie |
| Public types with zero callers | Delete candidates |

---

## 6. Assess template (paste into return for each type/package)

```
Scope: …
What it does (1 sentence):
Consumer today:
Architecture home (section):
Layer: vocabulary | logic | edge | infrastructure | test | sample
Public surface: [list public types]
Implementation hidden? Y/N — leaks:
Belongs here? Y/N — if N, recommend: delete | move to … | internalize
Aligns with modules=neurons+synapses? Y/N:
Dual path / god helper? …
Delete candidates: …
Move candidates: …
Verify: …
Grill 13: …
```

---

## 7. Exactly 200 agent cycles — wave plan

### Wave G0 — Inventory + ownership map (agents 1–16)

| Agents | Scope | Mission |
| --- | --- | --- |
| 1–2 | codegraph + public API inventory (src/modules/hosts/samples) | `own-audit`: residual ownership table into scorecard |
| 3–6 | Kernel purity pass | `own-audit` / `delete-trash` domain leaks |
| 7–10 | Contracts packages (all Built modules) | `contract-surface` |
| 11–14 | Package graph vs packages.md | `own-audit` |
| 15–16 | Scorecard + baseline HEAD | `docs-honesty` |

**Exit:** ownership map exists; zero silent Kernel domain types found or residual holds listed.

### Wave G1 — Module families (agents 17–64)

One module family (or Contracts+Runtime pair) per agent cluster:

| Agents | Family |
| --- | --- |
| 17–24 | AI (+ AI.Contracts + AI.Aspire.Hosting) |
| 25–32 | Tasks |
| 33–40 | Time (Countdown only; grill IReminder absence) |
| 41–48 | Google |
| 49–56 | Salesforce |
| 57–64 | Flutter (+ Flutter.Contracts + Flutter.Aspire.Hosting) |

**Exit:** each family answer: ships neurons/synapses? hides SDK? hosting optional package correct?

### Wave G2 — Cross-cutting packages (agents 65–96)

| Agents | Scope |
| --- | --- |
| 65–72 | Client + Abstractions + metapackage |
| 73–80 | Aspire + Aspire.Hosting |
| 81–88 | Security + Integrations.Mcp (+ Aspire.Hosting) |
| 89–96 | Testing library (public API honesty) |

### Wave G3 — Hosts + AppHost (agents 97–128)

| Agents | Scope |
| --- | --- |
| 97–104 | Ui host + UiEdgeContract |
| 105–112 | Mcp host + MapMcpHost public residual |
| 113–120 | AppHost + ProductSurfaceResources dual with McpHost |
| 121–128 | Silo Host + TestingAppHost + Quickstart hosts |

### Wave G4 — Samples + compositions (agents 129–148)

Compositions, Quickstart, AccountEnrichment — layer honesty; no Behavior rail lies.

### Wave G5 — Tests as architecture witnesses (agents 149–172)

Boundary / Hosting / Integrations / Module L1 — do tests **enforce** ownership or pin the wrong layer?

### Wave G6 — Docs honesty (agents 173–188)

architecture / packages / site pins / must-not-return — Built vs Designed; no Built-live lies.

### Wave G7 — Full gates + ownership scorecard close (agents 189–200)

Root build/test, docs npm, line-count, residual ownership table, hard stop at 200.

---

## 8. Orchestrator start now

1. Record HEAD/status/branch.  
2. codegraph ownership scan → residual table.  
3. Spawn Wave G0 (agents 1–16).  
4. After each wave: re-read HEAD/status; phase root gate when product moved.  
5. Prefer **delete / move / internalize** over new pin files.  
6. End at agent 200 with scorecard under  
   `docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md`  
   (durable close record — not a session checklist dump).

### Success is not

- “200 agents ran.”  
- “We added more Assert.Contains on source.”  
- “Gates green while public surface is a mess of implementation types.”  
- “Modules re-export SDKs for convenience.”  
- “Host owns business logic.”  
- “Overview refactor document with no file changes.”

### Success is

> **Every Built package assessed for ownership.**  
> **Public module surface ≈ neurons + synapses (+ hosting projection APIs).**  
> **Implementation details internal or deeper packages.**  
> **Kernel free of domain; compositions free of Kernel/runtimes.**  
> **Wrong-home types moved or deleted; dual product sentences gone.**  
> **Root gates green with quoted evidence.**  
> **Desktop product host still `WithFlutterHost()` — explicit, not accidental headless.**  
> **Behavior / calendar Time remain Designed — not faked Built.**

---

## 9. Subagent template (copy)

```
Wave G* agent K (mission: own-audit|move-home|encapsulate|delete-trash|contract-surface|host-edge|composition-layer|test-contract|docs-honesty|live-aspire)

Vision restatement: <one sentence>

Write scope: <exact paths>
Obey: CLAUDE.md; architecture §§1–3, 4.x, 4.6, 5; packages.md; scoring rule §1 of prompt-200-architecture-grill.md
Codegraph first: <query>
Protected: <list>
Must-not-return: ProbeHost, UiGateway-in-Kernel, IFlutter god, Behavior theater, Auto hosting,
  Dart→Orleans, tokens in journals, wholesale app/, mega-files >400 without Explicit hold,
  Kernel domain knowledge, Contracts→provider SDK, MCP-as-UI-bus

Actions:
1. codegraph + answer: what does it do? who consumes it?
2. Assess ownership with template §6
3. Fix wrong ownership autonomously when safe (delete/move/internalize)
4. Prefer architecture.md home over local habit
5. Verify: <dotnet test owning project; build; aspire if hosting product sentence>
6. Grill board §2 (13 answers)
7. Foreign dirty tree: leave unstaged

Do not expand scope. Do not invent Behavior rail. Do not claim green without output.
```

---

## 10. Verify commands

```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build

# Hosting-touched:
aspire stop --apphost hosts/DigitalBrain.AppHost
aspire start --project hosts/DigitalBrain.AppHost
# expect Desktop flutter host under shell/ when WithFlutterHost()
```

---

## 11. Logical grill patterns (agents apply)

### “Does it belong here?” decision tree

```
Is it a neuron interface or synapse record?
  yes → Contracts (or Abstractions if substrate-wide identity)
  no  → continue

Is it domain behavior implementing a neuron?
  yes → module runtime
  no  → continue

Is it provider SDK wiring / OAuth / transport?
  yes → runtime or Integrations.Mcp — never Contracts
  no  → continue

Is it HTTP/SSE/MCP host surface over IDigitalBrain?
  yes → hosts/*
  no  → continue

Is it AppHost resource composition?
  yes → AppHost / module Aspire.Hosting With*
  no  → continue

Is it multi-module ordinary C# over IDigitalBrain?
  yes → samples/compositions (client+contracts only)
  no  → continue

Is it test harness?
  yes → tests/* or DigitalBrain.Testing
  no  → GRILL: delete or invent architecture home first
```

### Recommendation form (before any move)

```
Recommendation: <delete | move to X | internalize | keep>
Strongest argument against:
Defense / fold:
Evidence (command or codegraph):
```

---

## 12. Residual scorecard file

Create/update:

`docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md`

Must include: HEAD baseline, cycle log, per-wave ownership findings, Explicit holds,
remaining wrong-home clusters, Desktop host live quote if re-proven, map of
public types that are not neurons/synapses/edges (justify or delete).

---

## 13. Relationship to test-truth campaign

| Prior (closed) | This campaign |
| --- | --- |
| Magic strings / density | Ownership / belonging |
| Test readability | Module encapsulation |
| Product const spine | Whether the type should exist at all |
| Residual holds in test-truth scorecard | May inform, not primary |

Do **not** re-run 200 de-string agents. If ownership fix needs a const move, do it surgically.

---

**Hard stop at agent 200.**  
If ownership still wrong or theater remains, residual table is honest — do not invent agent 201.
