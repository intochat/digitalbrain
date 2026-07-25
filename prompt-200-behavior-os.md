# MANDATORY: 200-agent DigitalBrain **Behavior OS** campaign
# (exactly 200 · framework = neurons+synapses · OS = behaviors · UI is behavior · BDD-first)

You are the **orchestrator** (Grok / Claude / Codex) in the DigitalBrain monorepo.
Hard budget: **exactly 200 subagent cycles**. Cycle = one subagent, **one write scope**, one
scoring rule, one grill, one verify. Waves of **8–12** non-overlapping scopes. Zero user menus
unless irreversible — then recommend hard and proceed only if architecture already decides.

This campaign **supersedes** pure ownership-grill as the primary mission. Prior campaign
(`prompt-200-architecture-grill.md`, scorecard
`docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md`) left gates green and
ownership cleaner. That work is **closed residual truth** — do not re-open pure ownership soup
unless Behavior OS work forces a move/delete.

**Primary question of every cycle (non-negotiable):**

> **Framework ships neurons + synapses and hides implementation.**  
> **Operating system is behaviors — behaviors describe *everything*, including UI.**  
> **Does this change grow the framework vocabulary, or the OS behavior surface?**  
> **Is the product sentence synapse-driven + BDD-proven — or host hand-wiring theater?**

Prefer **red BDD → green behavior → journal proof** over new host chrome. Prefer **one public
behavior identity** (namespace + class) over god shells. Prefer **broadcast facts** that Flutter
reacts to over imperative “start the app” host scripts.

---

## THE ONE VISION (every agent restates in one sentence)

> **A brain you program by writing ordinary C#, and that can program itself.**
>
> **Framework:** typed interface = surface, synapse = substrate, generator = bridge.  
> **Modules own vocabulary** (neuron interfaces + synapse records).  
> **Behaviors own logic** — including shell, login, first screen, and every product flow.  
> **The client API is the programming model** — same C# outside the cluster as a script and
> inside as an approved behavior.

### Split that must stay true

| Layer | Owns | Must not |
| --- | --- | --- |
| **Framework** (Kernel, Abstractions, Client, generator, Testing harness) | Neuron mechanics, journals, owner bounds, `IDigitalBrain` | Login chrome, scene trees, CRM, LLM prompts as product |
| **Modules** (`.Contracts` + runtime) | Domain vocabulary: `IShell`/`IScene`, `ILLM`, `IGmail`, … | Second client API; Behavior install rail; Flutter widgets in C# |
| **Operating system (Behaviors)** | Product logic over existing vocabulary — **including UI** | New synapse vocabulary without module rebuild; Kernel domain knowledge |
| **Edges** (Ui HTTP/SSE, Mcp, Flutter host) | Northbound projection of vocabulary / events | Business logic; Behavior theater that skips synapses |
| **BDD tests** | Product sentences on journals + edge + host projection | Pin private field names; mock Kernel grains as product contract |

### North-star product sentence (first vertical of this campaign)

When DigitalBrain **activates** for an owner:

1. Framework (or composition entry) **broadcasts a typed synapse** — e.g. `DigitalBrainActivated`
   (name exact after design grill; fact, not a method reply).
2. A **Flutter-facing behavior** (or the OS behavior that drives Flutter vocabulary) **reacts** to
   that fact — not to a host `main()` special-case.
3. The behavior **starts UI** through existing Flutter vocabulary (`IShell` / `IScene` / open-scene
   facts) — **renders the login / first screen**.
4. **BDD** proves the chain:

```gherkin
Given DigitalBrain is activated for an owner
When the activation synapse is committed
Then a Flutter OS behavior reacts
And the UI starts
And the first screen (login or shell home — design decides) is presented
```

Evidence oracles: **typed journals** (`SceneOpened` / activation fact), **Ui edge / SSE** where
Built, **Dart host projection** where Built — never “it compiled.”

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
git rev-parse HEAD   # after ownership-grill commit on agent/digitalbrain-hosting-testing
# tip at authoring: 8fde5963 (own(architecture): 200-agent ownership grill…)
```

Obey in order:

1. `CLAUDE.md` / `Claude.md` (gates, grilling, no narrative comments)
2. `docs/architecture.md` §§1–3, **module model**, **4.x**, **4.6 Flutter**, **§5 Behaviors**, 6–9, **11**
3. `docs/packages.md`
4. Ownership residual:
   `docs/superpowers/specs/2026-07-25-architecture-ownership-scorecard.md`
5. Hosting/testing design:
   `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md`
6. Must-not-return:
   `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md`

**Oracles:** compiler, package graph, test suite, git, **codegraph first**, Context7 / Microsoft
Learn / dart MCP / Aspire MCP. Fall back loudly.

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

1. **Framework purity** — change is neuron/synapse/journal/generator/client substrate, not UI logic
2. **Behavior OS honesty** — product logic lives as behavior identity (or clearly pre-rail composition
   marked for Behavior home); not host `Program.cs` business rules
3. **UI-is-behavior** — shell/login/first screen driven by vocabulary + behavior reaction, not
   imperative dual path in AppHost
4. **Synapse activation** — activation is a **broadcast fact**; Flutter/OS reacts; no name-dispatched
   “run behavior X” god API
5. **BDD as contract** — product sentence is Given/When/Then with journal/edge evidence
6. **Architecture alignment** — modules still own vocabulary; no silent Built vs Designed lie
7. **Encapsulation** — provider SDK / grain / OAuth stay off Contracts; Kernel domain-free
8. **Trash delete** — dual product sentences, god helpers, fake Behavior theater without rail
9. **Live proof** — only when product hosting sentence changes; quote aspire health
10. **Vision alignment** — ordinary C# that can install as behavior; human-approved rail when install
    is claimed Built

Forbidden:

- Invent `IFlutter` god neuron
- Widgets / BuildContext / Dart UI types in C# Contracts or Kernel
- MCP-as-UI bus; OTel-as-UI truth
- Behavior rail that **dispatches by name** instead of typed synapses
- Claiming Behavior **install/approval/rollback** Built without red→green + journaled rail
- Calendar `IReminder` invent
- Restoring ProbeHost / Auto hosting / UiGateway-in-Kernel
- “Green unit mocks” as substitute for journal BDD
- Kernel domain knowledge

### Module purity (unchanged hard table)

| Layer | Must | Must not |
| --- | --- | --- |
| **Contracts** | Public neuron interfaces + synapse records | Provider SDKs; host HTTP; Flutter widgets |
| **Runtime** | Neurons implementing contracts | Second client API |
| **Aspire.Hosting** | Resource projection | Hand-wire product Ui when `With*` exists |
| **Behaviors (new OS surface)** | Ordinary C# over `IDigitalBrain` + contracts | New vocabulary; Kernel refs; provider SDKs |
| **BDD tests** | Product sentences on facts/edge | Private field theater |

---

## 2. Grill board (every agent — 13 answers before done)

1. **What does this thing do** (one sentence)?
2. **Is it framework vocabulary, module neuron, OS behavior, edge, or test witness?**
3. **Who is the consumer today** (type, package, BDD scenario, or person)?
4. **Does architecture.md place it here?** Quote section or say “silent invent / deliberate extension.”
5. **If UI-related: which synapse does Flutter react to? Which vocabulary opens the screen?**
6. **What would break if we deleted it?**
7. **Does this invent Behavior install rail without human-approval design?**
8. **Does Kernel/Hosting learn a domain word it must not know?**
9. **Is the proof BDD + journal/edge, or compile-only theater?**
10. **What did I claim without a command?**
11. **What changed that I did not change?** (foreign dirty tree)
12. **Could this live one layer in / out?** (framework vs OS vs edge)
13. **Would a new engineer find the right home by reading vision + architecture alone?**

---

## 3. Each subagent prompt MUST include

1. Exact write scope (paths) — non-overlapping within the wave  
2. Architecture sections to obey  
3. Scoring rule (§1)  
4. Mission type: `design-behavior` | `bdd-red` | `behavior-impl` | `synapse-vocab` |
   `flutter-react` | `edge-project` | `rail-proposal` | `test-contract` | `docs-honesty` |
   `live-aspire` | `delete-trash`  
5. Codegraph first (or required first action)  
6. Verify commands (owning project first; root gate at phase boundaries)  
7. Grill answers (13)  
8. Protected surfaces (§4)  
9. Must-not-return list  
10. Vision restatement (one sentence)  
11. **Autonomous mandate:** if wrong home found, fix (delete/move/internalize/re-home to behavior)
    when safe; else residual hold with recommendation  
12. **No new public product API** unless red BDD (or Explicit hold) exists first  

---

## 4. Protected surfaces (surgical only)

- Kernel **behavior spine** (neuron mechanics) — may encapsulate; no domain knowledge in  
- Generator public contracts + alias wire names  
- Testing public API (`TestBrain`, AppHost fixtures) — may **add** BDD helpers if product requires  
- Built module **public neuron/synapse contracts**  
- Flutter first-five + dual golden (extend only with red + golden update)  
- Ui HTTP + SSE route shapes (constants OK; shape change needs red→green)  
- Explicit Desktop/Headless hosting API (`WithFlutterHost` / `WithFlutterHost<HeadlessHost>`)  
- Product AppHost may keep `WithUiEdge()` + `WithFlutterHost()` as **edge projection** — product
  *logic* must migrate toward behaviors, not grow in AppHost  
- Human-approved install rail design (when built: journaled, reversible)

---

## 5. Codegraph mandate

**Before editing**, each agent runs `codegraph_explore` on symbols in write scope and pastes:
what it does, callers, dependents, dual paths, public vs internal.

Orchestrator pre-scan (re-run every 2 waves):

| Cluster | Why grill |
| --- | --- |
| `docs/architecture.md` §5 Behaviors | Design home for rail |
| `samples/DigitalBrain.Compositions/**` | Pre-rail logic → Behavior home candidates |
| `hosts/DigitalBrain.Ui/**` + Flutter clients | Edge vs OS behavior split |
| `modules/**/Flutter.Contracts/**` | Vocabulary for screens (no widgets) |
| Activation / session / shell open paths | Synapse-driven boot chain |
| `src/DigitalBrain.Testing/**` | BDD / journal proof harness |
| Zero-consumer public types | Delete candidates |

---

## 6. Assess template (paste into return)

```
Scope: …
What it does (1 sentence):
Layer: framework | module-vocab | os-behavior | edge | test | docs
Consumer today:
Architecture home (section):
Activation synapse? (name / none / invent with red):
Flutter reaction path:
BDD scenario (Given/When/Then):
Public surface:
Implementation hidden? Y/N — leaks:
Belongs here? Y/N — if N: delete | move to … | internalize | re-home as behavior
Aligns with framework=neurons+synapses, OS=behaviors? Y/N:
Dual path / host hand-wire? …
Delete candidates: …
Verify: …
Grill 13: …
```

---

## 7. Exactly 200 agent cycles — wave plan

### Wave B0 — Design lock + BDD red (agents 1–16)

| Agents | Scope | Mission |
| --- | --- | --- |
| 1–2 | architecture §5 + ownership scorecard residual | `design-behavior`: write durable design delta for Behavior OS + activation synapse |
| 3–4 | activation fact design (`DigitalBrainActivated` or architecture-named) | `synapse-vocab` — Contracts vs Abstractions home grill |
| 5–6 | Flutter/UI reaction design (login/first screen) | `flutter-react` design only |
| 7–10 | BDD skeleton (red Explicit or failing proofs) | `bdd-red` |
| 11–12 | package graph / rail placement | `design-behavior` |
| 13–16 | design doc + scorecard bootstrap | `docs-honesty` |

**Exit:** design accepted in-repo; at least one red BDD that fails for activation→UI; no fake Built rail.

Create/update:

`docs/superpowers/specs/2026-07-25-behavior-os-design.md`  
`docs/superpowers/specs/2026-07-25-behavior-os-scorecard.md`

### Wave B1 — Framework substrate for behaviors (agents 17–48)

| Agents | Scope |
| --- | --- |
| 17–24 | Testing: BDD harness / journal matchers / scenario helpers (no product OS lie) |
| 25–32 | Client / Abstractions: observation needs for behavior proofs (Hold #7 honesty) |
| 33–40 | Kernel: only if activation/journal substrate requires — **no UI** |
| 41–48 | Generator / module capsule if behavior load needs compile hooks — grill hard |

**Exit:** BDD can observe activation + shell facts without mocking Kernel.

### Wave B2 — Vocabulary for OS UI boot (agents 49–72)

| Agents | Scope |
| --- | --- |
| 49–56 | Flutter.Contracts — only if first-screen needs new **thin** facts (prefer reuse `SceneOpened`) |
| 57–64 | Time/Tasks/AI — only if boot chain needs them; else skip with residual |
| 65–72 | Golden dual + Dart wire if contracts change |

**Exit:** vocabulary sufficient for login/first screen without IFlutter god.

### Wave B3 — Behavior implementations (agents 73–112)

| Agents | Scope |
| --- | --- |
| 73–80 | Activation broadcaster (who emits the fact — composition/behavior/host grill) |
| 81–88 | First OS behavior: react to activation → open login/first screen via `IShell` |
| 89–96 | Login/first-screen behavior body (ordinary C#, contracts only) |
| 97–104 | Migrate `samples/DigitalBrain.Compositions` shell pieces toward behavior home |
| 105–112 | AccountEnrichment / samples honesty — process sample vs OS behavior |

**Exit:** ordinary C# behavior reacts to activation synapse and drives Flutter vocabulary.

### Wave B4 — Flutter edge reacts (agents 113–140)

| Agents | Scope |
| --- | --- |
| 113–120 | Ui host / SSE: project facts behaviors need (edge only) |
| 121–128 | Dart pure host + shell chrome: react to edge events for first screen |
| 129–136 | Desktop `WithFlutterHost` product sentence remains explicit |
| 137–140 | live-aspire residual if product sentence changed |

**Exit:** Flutter side shows first screen when activation chain fires (L1 and/or Explicit live).

### Wave B5 — BDD green suite (agents 141–168)

| Agents | Scope |
| --- | --- |
| 141–148 | Green the activation→UI BDD (journals) |
| 149–156 | Edge/SSE BDD where applicable |
| 157–164 | Delete dual host hand-wire theater |
| 165–168 | Residual holds honest |

**Exit:** default gate includes non-Explicit BDD for the activation sentence (or Explicit hold with residual).

### Wave B6 — Docs + architecture honesty (agents 169–184)

| Agents | Scope |
| --- | --- |
| 169–176 | architecture.md §5 + §4.6 Built vs Designed update (only what is proven) |
| 177–180 | packages.md / site pins |
| 181–184 | must-not-return + CLAUDE.md residual if loop improved |

**Exit:** docs do not claim full Behavior install rail Built unless rail is proven.

### Wave B7 — Full gates + scorecard close (agents 185–200)

Root build/test, docs npm, line-count, residual table, hard stop at 200.

---

## 8. Orchestrator start now

1. Record HEAD/status/branch.  
2. codegraph + architecture §5 pre-scan.  
3. Spawn Wave B0 (agents 1–16).  
4. After each wave: re-read HEAD/status; phase root gate when product moved.  
5. Prefer **behavior + synapse + BDD** over host special cases.  
6. End at agent 200 with scorecard under  
   `docs/superpowers/specs/2026-07-25-behavior-os-scorecard.md`  
   and design under  
   `docs/superpowers/specs/2026-07-25-behavior-os-design.md`.

### Success is not

- “200 agents ran.”  
- “We added `IBehavior` empty interfaces without activation facts.”  
- “Host `Program.cs` opens login without a synapse.”  
- “Flutter widgets in C# Contracts.”  
- “Gates green while Behavior install is silently claimed Built.”  
- “Overview refactor document with no red BDD.”

### Success is

> **Framework remains neurons + synapses (modules own vocabulary).**  
> **OS product logic is behaviors — including UI boot.**  
> **Activation is a broadcast synapse; Flutter OS reacts and presents first screen.**  
> **BDD covers the product sentence with journal/edge evidence.**  
> **Human-approved install rail is Designed until proven — not faked.**  
> **Root gates green with quoted evidence.**  
> **Desktop product host still explicit `WithFlutterHost()`.**  
> **No IFlutter god; no Behavior-by-name dispatch.**

---

## 9. Subagent template (copy)

```
Wave B* agent K (mission: design-behavior|bdd-red|behavior-impl|synapse-vocab|flutter-react|edge-project|rail-proposal|test-contract|docs-honesty|live-aspire|delete-trash)

Vision restatement: Framework = neurons+synapses; OS = behaviors (including UI); activation is a synapse Flutter reacts to.

Write scope: <exact paths>
Obey: CLAUDE.md; architecture §§1–3, 4.x, 4.6, 5; packages.md; ownership scorecard residuals; scoring §1 of prompt-200-behavior-os.md
Codegraph first: <query>
Protected: <list>
Must-not-return: ProbeHost, UiGateway-in-Kernel, IFlutter god, Behavior-by-name dispatch,
  Auto hosting, Dart→Orleans, tokens in journals, widgets in C# Contracts, calendar IReminder invent,
  fake Built install rail, mega-files >400 without Explicit hold, Kernel domain knowledge

Actions:
1. codegraph + answer: framework or OS? which synapse?
2. Assess with template §6
3. Prefer red BDD before public surface
4. Fix wrong home autonomously when safe
5. Verify: <owning project; root at phase boundary>
6. Grill board §2 (13 answers)
7. Foreign dirty tree: leave unstaged

Do not expand scope. Do not claim green without output.
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
# expect first screen only after activation synapse path is Built
```

---

## 11. Logical grill patterns

### “Framework vs OS” decision tree

```
Is it a neuron interface or synapse record?
  yes → module Contracts (or Abstractions if substrate-wide identity)
  no  → continue

Is it durable domain implementation of a neuron?
  yes → module runtime
  no  → continue

Is it product logic over IDigitalBrain + contracts (including UI flow)?
  yes → Behavior OS (or pre-rail composition with explicit Behavior home residual)
  no  → continue

Is it HTTP/SSE/MCP/Dart host projection?
  yes → hosts/* or clients/*
  no  → continue

Is it AppHost resource composition?
  yes → AppHost / module Aspire.Hosting With*
  no  → continue

Is it test harness / BDD witness?
  yes → tests/* or DigitalBrain.Testing
  no  → GRILL: delete or invent architecture home first
```

### Activation chain (target shape)

```
DigitalBrain activated (owner-scoped)
  → emit DigitalBrainActivated (Synapse, broadcast)
  → OS behavior handles / reacts
  → IShell.Open(OpenScene(...)) or equivalent vocabulary
  → SceneOpened fact
  → Ui edge / Flutter projects first screen (login)
```

Exact type names, package homes, and whether login is first screen vs shell home are **design grill outputs** in Wave B0 — not silent invent mid-impl.

### Recommendation form (before any move)

```
Recommendation: <delete | move to X | internalize | keep | re-home as behavior>
Strongest argument against:
Defense / fold:
Evidence (command or codegraph or red test):
```

---

## 12. Residual scorecard file

Create/update:

`docs/superpowers/specs/2026-07-25-behavior-os-scorecard.md`

Must include: HEAD baseline, cycle log, per-wave findings, Explicit holds, activation synapse
name, first-screen decision, BDD scenario status (red/green), remaining dual host paths,
Desktop host live quote if re-proven, hard stop at 200.

Design record:

`docs/superpowers/specs/2026-07-25-behavior-os-design.md`

Must include: framework vs OS split, activation fact, Flutter reaction path, BDD examples,
install-rail Designed boundary, relationship to existing compositions.

---

## 13. Relationship to prior campaigns

| Prior | This campaign |
| --- | --- |
| Test-truth (closed) | Density / de-string |
| Architecture ownership grill (closed) | Belonging of packages/types |
| **This: Behavior OS** | **Build OS as behaviors; UI is behavior; synapse activation + BDD** |

Do **not** re-run 200 ownership-only agents. If ownership blocks Behavior OS, fix surgically and
record residual.

---

## 14. Explicit honesty about Built vs Designed

| Claim | Status at campaign start |
| --- | --- |
| Neurons + synapses + modules | **Built** |
| Flutter first vertical vocabulary + edge | **Built** (not Built-live full chrome) |
| Compositions pre-rail | **Built** (not installed Behaviors) |
| Behavior proposal / approval / install / rollback | **Designed — unbuilt** |
| Activation→Flutter first screen as Behavior OS | **This campaign’s target** — prove before docs say Built |
| Human-approved install rail | **Designed** until rail agents complete with evidence |

Agents must **not** rewrite architecture to claim full rail Built without green proofs.

---

**Hard stop at agent 200.**  
If the activation→login chain is incomplete, residual table is honest — do not invent agent 201.
