# MANDATORY: 200-agent DigitalBrain grill → trash → fix campaign
# (exactly 200 · grill-first · vision-aligned · autonomous delete/fix · no menus)

You are the **orchestrator** (Grok / Claude / Codex) in the DigitalBrain monorepo.
Hard budget: **exactly 200 subagent cycles**. Cycle = one subagent, one write scope, one scoring
rule, one grill, one verify. Waves of **8–12** non-overlapping scopes. Zero user menus unless
irreversible — then recommend hard and proceed only if approved in-repo docs already say so.

This is **not** “add features.” This is **adversarial alignment**: find trash, find architecture
lies, find vision collapses, delete or fix until the tree matches the vision.

---

## THE ONE VISION (non-negotiable — every agent must restate alignment in one sentence)

> **A brain you program by writing ordinary C#, and that can program itself.**
>
> **The OS UI is not a Flutter app with agents behind it.**
> **It is a brain whose UI vocabulary is a Flutter module, and whose logic
> (login, shell, session, windows, notifications, settings, multi-module apps)
> is compositions/behaviors over that vocabulary — the same way AccountEnrichment
> composes Gmail + Salesforce.**
>
> **The human sees synapses via a Flutter host. OS logic is compositions/behaviors
> over typed vocabulary — never a second kernel inside Dart.**

### Load-bearing corollaries

1. **Modules own vocabulary.** Contracts + neurons. Compile-time.
2. **Compositions / future Behaviors own logic.** One public class per file; `IDigitalBrain` + contracts only.
3. **Dart owns pixels only.** Projection of journals/descriptors — never domain ledger, never silo, never MCP tools as UI.
4. **Northbound truth path:**  
   `Flutter host → hosts/DigitalBrain.Ui (HTTP/SSE) → IDigitalBrain → silo + FlutterModule journals`
5. **Module-owned hosting:** selecting `FlutterModule` with `WithUiEdge` / `WithFlutterHost` composes OS surface. Aspire is orchestrator; DigitalBrain owns the product sentence.
6. **Journals are durable truth.** OTel is diagnostics. Never OTel-driven product UI.
7. **MCP is peer agent edge**, not product UI bus.

### Fold conditions (vision collapse — delete or reverse)

| Temptation | Fold |
| --- | --- |
| Aspire-only Flutter/Ui with no `FlutterModule` implication | Incomplete packaging |
| Dart → Orleans / Kernel / journals / reminders | Second kernel |
| Flutter talks MCP tools as product UI | Wrong northbound path |
| `IFlutter` god neuron / central desktop grain | Vocabulary collapse |
| Behavior rail invented without proofs | Theater |
| Restore `app/` wholesale | Architecture regression |
| UiGateway / dual protos / gRPC UI vocabulary | Rejected |
| Tokens/secrets in journals | Never |
| Narrative `/// <summary>` spam / commented-out code | CLAUDE.md |
| “Tests pass” without quoting command output | False claim |

---

## 0. Ground truth (every wave)

```
git rev-parse HEAD
git status --porcelain
git branch --show-current
```

If porcelain dirty and **you** did not dirty it: surface and stop that path. Do not revert foreign WIP.
Do not sweep it into your commits.

**Baseline at prompt authoring:** record HEAD when you start. Re-record every wave.

Obey in order:

1. `CLAUDE.md` / `Claude.md` (gates, grilling, no narrative comments)
2. `docs/architecture.md` §§1, 3, **4.6 Flutter**, **5 Behaviors**, 6–9, **11**
3. `docs/packages.md`
4. `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md`
5. `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md` (must-not-return)
6. Prior campaign commits on this branch (module hosting, headless host, compositions) — **do not re-claim as missing**; grill them for residual trash

Oracles: **compiler, test suite, git**. Prefer codegraph → Context7 / Microsoft Learn / dart MCP / Aspire MCP.
Fall back loudly. **ALWAYS verify APIs via Context7 or compiler before inventing.**

**Root gate before any “done” claim (never `--filter` for completion):**

```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

Plus Dart/Flutter when those trees change:

```
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
# from each package:
dart test
# when Flutter SDK present (expected: E:\tools\flutter on PATH):
flutter analyze
flutter test
# Windows (when windows/ exists):
flutter build windows
# optional L3: flutter run -d windows
```

---

## 1. Scoring rule (copy into every subagent — exact)

Allowed only if ≥1:

1. **Architecture truth** — docs/code lie about Flutter/Behaviors/host/module hosting/compositions
2. **Missing consumer proof** — Built without L0/L1/L2 (or Explicit)
3. **Zero-consumer trash → delete**
4. **Framework misuse** — Flutter/Dart/Orleans/Aspire vs official docs / Context7
5. **Boundary violation** — Kernel purity, packages.md, Dart↔Orleans, compositions↔Kernel
6. **Vision alignment** — modules vs behaviors; no second runtime; module owns OS surface composition
7. **Historical recovery value** — restores proven live loop without rejected architecture
8. **Module-owned hosting** — reduces AppHost hand-wiring; Flutter depends on DigitalBrain selection

Forbidden: resurrect ProbeHost/UiGateway-in-Kernel; ship Behavior rail without architecture+proofs;
widgets in C#; MCP tool dicts on UI contracts; login grain as IdP; OTel as UI truth;
Aspire-only Flutter; Dart→Kernel; inventing chrome without consumer.

**Trash definition (delete when found):**

- Code with no consumer today and no failing Explicit proof held for a near consumer
- Dead paths, husks, dual implementations of the same product sentence
- Docs claiming Built when only Designed (or reverse)
- God types, second ledgers, second kernels
- Comments that restate signatures
- Session logs / task checklists posing as architecture

---

## 2. Grill board (every agent answers before claiming done)

1. What has no consumer today?
2. What did I claim without a command?
3. What changed that I did not change?
4. Modules = vocabulary, compositions = logic, module Aspire.Hosting = surface composition?
5. Could this avoid Kernel changes? (prefer yes)
6. Is the sample still a valid future Behavior file?
7. Would v0.1.18 live MCP→UI **intent** work on this path (re-bound to Ui HTTP, not kernel gRPC)?
8. Does the Flutter host start because DigitalBrain selected `FlutterModule` — or AppHost folklore?
9. If someone removes `AddModule<FlutterModule>` (or host options), do OS surface resources correctly disappear?
10. Did I **delete** more than I added when possible?

---

## 3. Each subagent prompt MUST include

1. Exact write scope (paths) — non-overlapping within the wave  
2. Architecture sections to obey (§4.6 + family layout + packages.md)  
3. Scoring rule (copy §1)  
4. Mission type: `adversarial` | `delete` | `proof` | `fix` | `docs-honesty`  
5. TDD: failing proof first for any Built behavior change  
6. Verify commands + grill answers (10 questions)  
7. Protected surfaces (see §4)  
8. Foreign dirty tree → leave unstaged  
9. Must-not-return list  
10. Vision quote restatement (one sentence alignment)  
11. Module-owned hosting check  
12. **Autonomous mandate:** if trash or bad decision is found in scope, fix or delete in the same cycle — do not only report  

---

## 4. Protected surfaces (do not casually rewrite)

- Kernel spine, generator, Testing path (`TestBrain`, AppHost fixtures)
- Built modules: AI, Tasks, Time Countdown, Google, Salesforce, AccountEnrichment
- Flutter contracts first-five types without red→green
- Ui edge HTTP + SSE contract (unless proof of bug)
- Dual golden wire pin
- Module hosting product sentence once green L0 pins exist

Protected ≠ unreviewable: agents may grill them and open Explicit red proofs; mass rewrite requires
evidence and phase-boundary commit grill.

---

## 5. Known ground (as of 2026-07-25 campaign — grill residual, do not re-build as greenfield)

| Item | Claimed status | Agent duty |
| --- | --- | --- |
| Flutter vocabulary + L1 journals | Built | Adversarial: husks, god types, golden drift |
| Ui edge open-scene + SSE | Built | Adversarial: dual vocabulary, secrets, OTel |
| `Flutter.Aspire.Hosting` WithUiEdge/WithFlutterHost | Built | Adversarial: hand-wire leftovers, WaitFor, env leaks |
| Headless Dart host + Auto mode | Built | Adversarial: fake Flutter, PATH lies, journal env |
| Compositions: OpenHome, PostAuth, Navigate, Countdown, Enrichment surface, AiPane | Built (samples) | Adversarial: Kernel refs, not Behavior-shaped, zero consumer |
| Windows Flutter widget chrome | Designed | **Implement only with Flutter SDK + L0/L1**; no fake |
| Behavior install rail | Designed | **Do not invent** |
| Descriptor algebra beyond first vertical | Open | Only if live vertical consumer exists |

**Flutter SDK (this environment):** installed at `E:\tools\flutter` (stable), User PATH updated,
`flutter build windows` smoke-proven. Agents must re-check `flutter doctor` / `flutter devices` before
Windows claims.

---

## 6. Exactly 200 agent cycles — wave plan

### Wave G0 — Inventory + adversarial map (agents 1–24)

Parallel non-overlapping:

| Agent | Scope | Mission |
| --- | --- | --- |
| 1–3 | `docs/architecture.md` §4.6 vs code | docs-honesty: Built/Designed lies |
| 4–6 | `docs/packages.md` + PackableProjects + package graph L0 | proof / docs-honesty |
| 7–9 | `hosts/DigitalBrain.AppHost` + Quickstart/Testing AppHosts | adversarial: hand-wire, dual paths |
| 10–12 | `modules/**/Flutter*` + Aspire.Hosting | adversarial: Kernel leak, env secrets, WaitFor |
| 13–15 | `hosts/DigitalBrain.Ui` + Ui.Tests | adversarial: second protocol, ProbeHost smell |
| 16–18 | `clients/digitalbrain_*` | adversarial: Orleans refs, dead code, dual golden |
| 19–21 | `samples/DigitalBrain.Compositions` + boundary L0 | adversarial: not Behavior-shaped |
| 22–24 | git history `v0.1.18` / demolish SHAs — recovery score only | delete-or-adapt map (no wholesale restore) |

**Exit:** written residual trash list committed only if durable (architecture/packages). No mass chrome yet.

### Wave G1 — Delete trash (agents 25–48)

Each agent: one path cluster; **net reduction preferred**.

- Dead files, commented-out code, unused public APIs with no consumer
- Dual product sentences (two ways to start Ui)
- Doc paragraphs that contradict code
- Fake “Built” claims

TDD: if deleting might break a consumer, write failing proof first or hold Explicit.

### Wave G2 — Hosting + AppHost alignment (agents 49–72)

- Production AppHost only via module hosting for OS surface
- L0 pins that omit module ⇒ no `digitalbrain-ui` / flutter host
- Env graph: Ui = AsClient + owner; Flutter host = edge URL + shell only
- Fix any residual hand-wire or wrong WaitFor
- Auto/Headless/FlutterDesktop honesty when SDK present

### Wave G3 — Dart/Flutter host alignment (agents 73–104)

- `clients/digitalbrain_flutter`: pure projection + headless host remain correct
- **Windows platform:** only if `flutter doctor` shows Windows device — then:
  - add `windows/` via `flutter create` **without** resurrecting `app/` architecture
  - pixels from `ShellSurfaceController` / SSE only
  - `DIGITALBRAIN_UI_BASE` / `fromEnvironment`
  - no Orleans, no MCP tool client
- Dual golden still green
- `flutter analyze` / `flutter test` / `flutter build windows` when platform exists

### Wave G4 — Edge + journal live path (agents 105–128)

- C# L1: composition or MCP mutates → journal → Ui SSE projects
- Dart: SSE parse → surface controller (no restart)
- Integration fails if “brain moved, UI dead”
- Integration fails if “module not selected but surface resources exist”
- No OTel as product path

### Wave G5 — Compositions depth & boundary (agents 129–152)

- Grill each composition file as future Behavior file
- L0 forbids Kernel/runtimes/Integrations in Compositions.csproj
- Delete zero-consumer composition theater
- Enrichment/AI/Countdown/Navigate remain honest (OS scene vs multi-module L1 split)

### Wave G6 — Kernel/packages purity pass (agents 153–168)

- Kernel free of Flutter/UI
- Contracts free of Dart/Flutter SDK
- Hosting free of Kernel public API
- Packable inventory matches reality
- Alias pins if touched

### Wave G7 — Docs, site, CLAUDE honesty (agents 169–184)

- architecture §4.6 Built vs Designed accurate
- packages.md family table
- site tests green
- CLAUDE.md status only if loop improves
- Delete session logs / checklists posing as design

### Wave G8 — Full gates + residual scorecard (agents 185–200)

- Root gate (build + test, no filter)
- docs npm test/build
- Dart + Flutter gates
- Scorecard: cycles used (must total **200**), commits, live demo commands, still Designed, trash deleted count
- **Hard stop at 200.** If gaps remain, list them — do not silently continue as “almost 200.”

---

## 7. Orchestrator start now

1. Record HEAD/status/branch.  
2. Confirm Flutter: `flutter doctor`, `flutter devices` (expect `windows`).  
3. Spawn Wave G0 (agents 1–24) in parallel, non-overlapping.  
4. After each wave: re-read HEAD/status; stabilize Release build of touched projects; phase-boundary root gate when architecture/hosting changes.  
5. Prefer **delete + fix** over new surface area. Prefer journaled path + module selection proofs over pixel polish.  
6. End at agent 200 with scorecard.

### Success is not

- “200 agents ran.”
- “Flutter looks pretty.”
- “Aspire can start Flutter.”

### Success is

> **Trash is gone. Lies are gone. Bad architecture decisions are reversed or Explicitly held red.**  
> **The OS is the brain. Flutter is how humans see synapses. Compositions are how the OS thinks.**  
> **Selecting `FlutterModule` composes the surface because DigitalBrain owns it.**  
> **Gates are green with quoted evidence.**

---

## 8. Subagent template (copy)

```
Wave N agent K (mission: adversarial|delete|proof|fix|docs-honesty)

Vision restatement: <one sentence>

Write scope: <exact paths>
Obey: CLAUDE.md; architecture §4.6; packages.md; scoring rule §1 of prompt-200-grill.md
Protected: <list>
Must-not-return: ProbeHost, UiGateway-in-Kernel, IFlutter god, Behavior theater, Aspire-only Flutter,
  Dart→Orleans, tokens in journals, wholesale app/

Actions:
1. Inventory scope against vision + scoring rule
2. List trash / bad decisions with evidence (git/codegraph/compiler/tests)
3. Fix or delete autonomously in scope (net reduction preferred)
4. TDD: red proof first for Built behavior
5. Verify: <commands>
6. Grill board §2 answers in commit message if committing
7. Foreign dirty tree: leave unstaged

Do not expand scope. Do not invent Behavior rail. Do not claim green without output.
```

---

## 9. Live demo commands (update as you prove; start honest)

```
# Domain gate
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"

# Dart
cd clients/digitalbrain_wire && dart test
cd clients/digitalbrain_flutter && dart test

# Flutter (SDK: E:\tools\flutter on PATH)
flutter doctor
flutter devices
# when windows/ exists on digitalbrain_flutter:
flutter analyze
flutter test
flutter build windows

# Aspire product sentence
aspire run --project hosts/DigitalBrain.AppHost
# expect: silo + digitalbrain-ui (+ digitalbrain-flutter Auto: Flutter desktop or headless dart)
# POST open-scene or composition; SSE / host shows SceneOpened without restart
```

---

## 10. Hard stop

**Agent 200 is the hard stop.** Produce scorecard:

| Field | Content |
| --- | --- |
| Cycles used | 200 (or less if exhausted early with empty waves — say so) |
| Commits | SHAs + one-line grill |
| Trash deleted | paths / net LOC if measured |
| Bad decisions reversed | list |
| Still Designed | honest list |
| Live demo | exact commands that work |
| Gate evidence | build/test exit codes + quoted tail |
| Residual gaps | no silent “almost done” |

---

**END — 200 AGENTS — GRILL · TRASH · FIX · VISION ALIGNMENT**
