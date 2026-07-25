# MANDATORY: 200-agent DigitalBrain grill → trash → split → refactor → live Aspire
# (exactly 200 · grill-first · 400-line hard cap · folder/namespace discipline · aspire must work)

You are the **orchestrator** (Grok / Claude / Codex) in the DigitalBrain monorepo.
Hard budget: **exactly 200 subagent cycles**. Cycle = one subagent, one write scope, one scoring
rule, one grill, one verify. Waves of **8–12** non-overlapping scopes. Zero user menus unless
irreversible — then recommend hard and proceed only if approved in-repo docs already say so.

This campaign **supersedes** the prior 2026-07-25 grill. That wave left unit gates green while
**`aspire start` / product topology was still broken or unusable**. Treat that outcome as a
**failed success criterion**, not a foundation to polish. Prefer delete + split + re-homing over
new surface area. Prefer a live AppHost over another L0 pin theater.

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
8. **Live topology is part of Built.** A claim that hosting is Built while `aspire start` cannot show silo + Ui (+ Flutter host) healthy is a **lie**. Fix or demote the claim.

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
| “Hosting Built” while Aspire topology is red/unusable | False claim |
| File **> 400 lines** left unsplit without Explicit hold | **Trash** |
| Flat project dump of many types in one folder with no namespace/folder map | Layout trash |
| Mega-test class that is really N independent contracts | Test dump trash |

---

## 0. Ground truth (every wave)

```
git rev-parse HEAD
git status --porcelain
git branch --show-current
```

If porcelain dirty and **you** did not dirty it: surface and stop that path. Do not revert foreign WIP.
Do not sweep it into your commits.

**Baseline at prompt authoring (record and re-record every wave):**

```
git rev-parse HEAD   # authoring tip was 6a93ee79 on agent/digitalbrain-hosting-testing; re-read live
```

Obey in order:

1. `CLAUDE.md` / `Claude.md` (gates, grilling, no narrative comments)
2. `docs/architecture.md` §§1, 3, **4.6 Flutter**, **5 Behaviors**, 6–9, **11**
3. `docs/packages.md`
4. `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md`
5. `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md` (must-not-return)
6. Prior campaign scorecard is **historical only** — do not treat its “gates green” as product-live proof

Oracles: **compiler, test suite, git, Aspire live topology**. Prefer codegraph → Context7 /
Microsoft Learn / dart MCP / Aspire MCP. Fall back loudly.
**ALWAYS verify APIs via Context7 or compiler before inventing.**

### Hard gates (never `--filter` for completion claims)

**Domain (always):**

```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

**Live product (this campaign’s primary success — not optional theater):**

```
# Prefer aspire skill / MCP when available
aspire stop --apphost hosts/DigitalBrain.AppHost   # if stale
aspire start --project hosts/DigitalBrain.AppHost  # or repo-standard aspire run equivalent
aspire ps
# Resource health must be quoted: silo Healthy, digitalbrain-ui Healthy,
# digitalbrain-flutter Healthy OR honest Headless when Auto chooses dart,
# digitalbrain-mcp Healthy when selected.
# Prove northbound:
#   POST /shells/{shell}/scenes  → 202
#   GET  /shells/{shell}/events  → SSE scene-opened after mutator
```

**Dart/Flutter when those trees change:**

```
# Flutter SDK expected: E:\tools\flutter (prepend to PATH if missing)
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
# per package: dart test / flutter test
flutter analyze
flutter test
# when windows/ exists:
flutter build windows
```

**Line-count gate (new — fails the campaign if ignored):**

```
# Any product/test source *.cs / *.dart (exclude bin/obj/node_modules/.dart_tool/build/platform embedders)
# with physical line count > 400 is FAIL unless it has an [Fact(Explicit = true)] hold naming why
# and a tracked split plan in the residual scorecard.
```

Authoring inventory of **known >400-line trash** (must be split or Explicitly held — re-scan every wave):

| Lines (approx) | Path | Split mandate |
| --- | --- | --- |
| ~561 | `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs` | Split by concern (propose/approve/invoke/state) into folders + namespaces under module |
| ~559 | `modules/DigitalBrain.Modules.Tasks/TaskNeuron.cs` | Partial classes or neighbor types by lifecycle stage under `Tasks/` folders |
| ~505 | `tests/DigitalBrain.Tests/FlutterHostingProjectionContracts.cs` | One concern per file under `tests/.../Hosting/` or similar |
| ~471 | `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs` | Generator helpers by phase; keep public surface stable |
| ~442 | `modules/DigitalBrain.Modules.Time/CountdownNeuron.cs` | Lifecycle stages / recovery helpers split |
| ~422 | `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs` | Split by boundary family (Kernel, Hosting, Contracts, Packable) |

Soft watch (300–400 lines — split if growing or multi-concern):

- `src/DigitalBrain.Testing/Journals/TestJournal.cs`
- `tests/DigitalBrain.Time.Tests/CountdownLifecycle.cs`
- `src/DigitalBrain.Kernel/Neuron/Neuron.Capability.cs` (Kernel partials already exist — keep under 400)
- `tests/DigitalBrain.Integrations.Tests/McpEdge.cs`

---

## 1. Scoring rule (copy into every subagent — exact)

Allowed only if ≥1:

1. **Architecture truth** — docs/code lie about Flutter/Behaviors/host/module hosting/compositions/live Aspire
2. **Missing consumer proof** — Built without L0/L1/L2 (or Explicit) **or Built without live topology proof when hosting is claimed**
3. **Zero-consumer trash → delete**
4. **Framework misuse** — Flutter/Dart/Orleans/Aspire vs official docs / Context7
5. **Boundary violation** — Kernel purity, packages.md, Dart↔Orleans, compositions↔Kernel
6. **Vision alignment** — modules vs behaviors; no second runtime; module owns OS surface composition
7. **Historical recovery value** — restores proven live loop without rejected architecture
8. **Module-owned hosting** — reduces AppHost hand-wiring; Flutter depends on DigitalBrain selection
9. **File size / cohesion** — any source file **> 400 lines** is trash until split (or Explicit hold with residual entry)
10. **Layout / namespace honesty** — types live in folders that match namespaces and architecture family (module contracts / runtime / hosting; kernel filters/outbox/neuron; tests by family)

Forbidden: resurrect ProbeHost/UiGateway-in-Kernel; ship Behavior rail without architecture+proofs;
widgets in C#; MCP tool dicts on UI contracts; login grain as IdP; OTel as UI truth;
Aspire-only Flutter; Dart→Kernel; inventing chrome without consumer; “green unit tests, red Aspire”
as a done state; mega-files as “temporary.”

### Trash definition (delete or split when found)

- Code with no consumer today and no failing Explicit proof held for a near consumer
- Dead paths, husks, dual implementations of the same product sentence
- Docs claiming Built when only Designed (or reverse) **or claiming live product when Aspire is red**
- God types, second ledgers, second kernels
- Comments that restate signatures
- Session logs / task checklists posing as architecture
- **Any `*.cs` / `*.dart` product or test source > 400 lines** (platform embedder scaffolding under `windows/`/`android/`/`ios/`/`linux/`/`macos/` excluded; generated `*.g.cs` excluded only if truly generated and not hand-edited)
- Flat “junk drawer” folders where unrelated types share a directory without namespace/folder map
- Dual product sentences (two ways to start Ui / two client connect paths / two SSE parsers)

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
11. **Did I leave any file in scope > 400 lines?** (if yes: split now or Explicit hold + residual)
12. **Do folders/namespaces match architecture family layout after my edit?**
13. **Did I prove live Aspire health for any hosting claim I touched?** (quote `aspire ps` / resource state / logs)

---

## 3. Each subagent prompt MUST include

1. Exact write scope (paths) — non-overlapping within the wave  
2. Architecture sections to obey (§4.6 + family layout + packages.md)  
3. Scoring rule (copy §1)  
4. Mission type: `adversarial` | `delete` | `split` | `refactor-layout` | `proof` | `fix` | `live-aspire` | `docs-honesty`  
5. TDD: failing proof first for any Built behavior change  
6. Verify commands + grill answers (13 questions)  
7. Protected surfaces (see §4)  
8. Foreign dirty tree → leave unstaged  
9. Must-not-return list  
10. Vision quote restatement (one sentence alignment)  
11. Module-owned hosting check  
12. **400-line check** on every touched file after edit  
13. **Layout check** — if you add types, place them under the correct folder + namespace  
14. **Autonomous mandate:** if trash or bad decision is found in scope, fix/delete/**split** in the same cycle — do not only report  

---

## 4. Protected surfaces (do not casually rewrite)

- Kernel **behavior** spine (may **split** files / re-folder for layout; do not change product sentences without red→green)
- Generator public contracts (may split implementation; pin golden/outputs)
- Testing path public API (`TestBrain`, AppHost fixtures) — surgical only
- Built modules’ public neuron contracts without red→green
- Flutter contracts first-five types without red→green
- Ui edge HTTP + SSE route shapes (unless proof of bug)
- Dual golden wire pin
- Module hosting product sentence once green L0 pins exist **and** live Aspire proves it

Protected ≠ unreviewable: agents **must** split mega-files and fix live Aspire. Mass behavior rewrite
requires evidence and phase-boundary commit grill.

---

## 5. Layout and namespace rules (mandatory refactor discipline)

### Product C#

| Family | Project roots | Namespace / folder expectation |
| --- | --- | --- |
| Kernel | `src/DigitalBrain.Kernel` | `DigitalBrain.Kernel.*` under `Neuron/`, `Filters/`, `Outbox/`, `Hosting/`, `Serialization/` — already partial; keep each file ≤400 |
| Abstractions | `src/DigitalBrain.Abstractions` | leaf contracts only |
| Client / Aspire | `src/DigitalBrain.Client`, `Aspire`, `Aspire.Hosting` | one concern per file; hosting projections not public Kernel |
| Module contracts | `modules/...Contracts` | vocabulary types only; no Dart/Flutter SDK |
| Module runtime | `modules/DigitalBrain.Modules.{Family}` | neurons + module marker; **split fat neurons into folders** (`Salesforce/Propose`, `Tasks/Lifecycle`, …) matching namespaces |
| Module hosting | `modules/...Aspire.Hosting` | thin extensions; WaitFor/env exclusive |
| Hosts | `hosts/*` | edge/process only; no domain logic god files |
| Samples | `samples/*` | compositions = one public class per file; Behavior-shaped |
| Tests | `tests/*` | **one proof family per file**; prefer `tests/.../Hosting/`, `Boundary/`, `Journals/` folders over mega `*Contracts.cs` |

### Dart / Flutter

| Package | Layout |
| --- | --- |
| `clients/digitalbrain_wire` | DTOs + golden pin tests only |
| `clients/digitalbrain_flutter` | `lib/src/` by concern (`edge/`, `projection/`, `chrome/`, `env/`); `bin/` headless; `windows/` embedder only |

### Split recipe (when file > 400 lines)

1. **Inventory concerns** in the file (methods / nested types / regions of behavior).  
2. **Write or move proofs first** if public behavior might change (TDD).  
3. **Extract** to new files: prefer `partial` only when it is one type; prefer new types when concerns are separable.  
4. **Folder + namespace** must agree (`DigitalBrain.Tasks.Lifecycle` ↔ `.../Tasks/Lifecycle/`).  
5. **No narrative comments** as glue. Names carry meaning.  
6. **Re-count lines** — every resulting file ≤400.  
7. **Verify** owning project tests + any root gate required by phase.

### Project split (when a csproj is a junk drawer)

Split **projects** only when packages.md / architecture family says the boundary is real (Contracts vs
Runtime vs Aspire.Hosting already). Do **not** invent random projects. Do **re-folder inside** a
project when types are co-packaged correctly but co-located badly.

---

## 6. Known ground (2026-07-25 post-campaign — assume residual trash + broken live path)

| Item | Prior claim | Agent duty this campaign |
| --- | --- | --- |
| Flutter vocabulary + L1 journals | Built | Adversarial + split fat test pins |
| Ui edge open-scene + SSE | Built | **Live Aspire prove**; dual path hunt |
| `Flutter.Aspire.Hosting` With* | Built | **Live prove**; WaitFor/env/Auto honesty |
| Headless + Windows chrome | Built (first vertical) | Live host; no second kernel |
| Compositions samples | Built (pre-rail) | Boundary + Behavior shape; no rail invent |
| Unit root gate | Was green | Necessary **not sufficient** |
| **`aspire start` product topology** | **Not proven / user reports trash** | **P0 — diagnose + fix until healthy** |
| Mega-files >400 lines | Present | **P0 split wave** |
| Behavior install rail | Designed | **Do not invent** |
| Calendar Time / supervised AI | Designed | **Do not invent** |

**Flutter SDK:** `E:\tools\flutter` (stable). Re-check `flutter doctor` / `flutter devices` before Windows claims.

**Aspire:** use project Aspire skill/MCP. User may already have a stale AppHost running — stop/restart
cleanly; quote PIDs and resource states. Do not claim live green from a zombie process.

---

## 7. Exactly 200 agent cycles — wave plan

### Wave L0 — Live Aspire diagnose + stabilize (agents 1–24)

**Primary mission: make product topology real.**

| Agents | Scope | Mission |
| --- | --- | --- |
| 1–4 | `hosts/DigitalBrain.AppHost` + Aspire run/logs | `live-aspire`: start clean; inventory red resources |
| 5–8 | `hosts/DigitalBrain.Host` + silo health | fix silo start / module selection / journal storage |
| 9–12 | `hosts/DigitalBrain.Ui` + Ui.Tests | edge comes up; HTTP/SSE works against live silo |
| 13–16 | `modules/**/Flutter.Aspire.Hosting` + flutter host process | Auto/Headless/Desktop honesty; WaitFor; env |
| 17–20 | `hosts/DigitalBrain.Mcp` | peer edge healthy; not UI bus |
| 21–24 | end-to-end: mutator → journal → SSE → host | live proof; quote logs |

**Exit:** quoted `aspire ps` with silo + ui (+ flutter host) healthy **or** Explicit residual with
failing proof held. Docs must not say Built for hosting if still red.

### Wave L1 — Hard 400-line split (agents 25–56)

Each agent owns **one mega-file** (or one half of a split pair). Non-overlapping.

Priority order:

1. `Salesforce.cs`  
2. `TaskNeuron.cs`  
3. `FlutterHostingProjectionContracts.cs`  
4. `PackageBoundaryContracts.cs`  
5. `CountdownNeuron.cs`  
6. `DispatchManifestGenerator.cs`  
7. Soft 300–400 watch list  

Rules: net complexity down; public API stable unless red→green; folders/namespaces correct; every
output file ≤400 lines.

### Wave L2 — Project folder / namespace refactor (agents 57–88)

- Kernel: ensure `Neuron/`, `Filters/`, `Outbox/`, `Hosting/`, `Serialization/` stay coherent; no new god folder  
- Modules AI/Tasks/Time/Google/Salesforce/Flutter: runtime types in concern folders  
- Tests: break mega L0 classes into `tests/DigitalBrain.Tests/{Hosting,Boundary,Flutter,Packages}/`  
- Clients: `lib/src/{edge,projection,chrome,env}/` if not already  
- Update usings / internals / public API only as needed; root gate after clusters  

### Wave L3 — Dual paths / trash delete (agents 89–112)

- Dead files, commented-out code, unused public APIs  
- Dual client connect, dual SSE parsers, dual AppHost OS surface sentences  
- Zero-consumer theater  
- Net reduction preferred  

### Wave L4 — Hosting + AppHost alignment (agents 113–136)

- Production OS surface **only** via `FlutterModule` + `WithUiEdge`/`WithFlutterHost`  
- Omit module ⇒ no surface resources (L0 + live)  
- Env: Ui = AsClient + owner; Flutter = `DIGITALBRAIN_UI_BASE` + `DIGITALBRAIN_SHELL` only  
- Companion AppHosts stay vocabulary/silo-only  
- Re-prove live Aspire after hosting edits  

### Wave L5 — Edge + Dart projection (agents 137–156)

- Ui HTTP/SSE pure journal path (no OTel product UI)  
- Dart pure projection; dual golden green  
- Multi-event SSE without restart  
- Windows chrome remains pixels-only  

### Wave L6 — Compositions + boundaries (agents 157–172)

- Each composition = future Behavior file shape  
- L0 forbids Kernel/runtimes/Integrations  
- OS-scene vs multi-module honesty  
- No Behavior rail invent  

### Wave L7 — Kernel/packages purity + docs honesty (agents 173–188)

- Kernel free of Flutter/UI  
- Contracts free of Dart/Flutter SDK  
- Hosting free of Kernel public API  
- Packable inventory matches packages.md  
- architecture/packages/CLAUDE honesty for **live** vs unit-only Built  
- Delete session logs posing as design  

### Wave L8 — Full gates + live demo + scorecard (agents 189–200)

- Root domain gate (build + test, no filter)  
- docs npm test/build  
- Dart + Flutter gates  
- **Live Aspire demo commands with quoted healthy resources**  
- Line-count scan: **zero** unexplained files >400 lines  
- Scorecard: cycles 200, commits, trash/splits, still Designed, residual gaps  
- **Hard stop at 200**

---

## 8. Orchestrator start now

1. Record HEAD/status/branch.  
2. Confirm Flutter + Aspire CLI (`aspire --version`, `flutter doctor`).  
3. **Stop stale AppHosts** if user has zombie topology; start clean for L0.  
4. Spawn Wave L0 (agents 1–24) in parallel non-overlapping scopes.  
5. After each wave: re-read HEAD/status; line-count scan on touched trees; phase-boundary root gate when hosting/architecture changes; **re-run live Aspire when hosting touched**.  
6. Prefer **delete + split + re-folder** over new surface.  
7. End at agent 200 with scorecard.  

### Success is not

- “200 agents ran.”  
- “Unit tests pass while Aspire is red.”  
- “We added more L0 pins.”  
- “Mega-file is documented.”  
- “Flutter looks pretty.”

### Success is

> **Trash is gone. Mega-files are split. Folders/namespaces match architecture.**  
> **Lies about Built/live are gone.**  
> **`aspire start` product topology works with quoted health.**  
> **Northbound path is live: mutator → journal → SSE → host.**  
> **Selecting `FlutterModule` composes the surface because DigitalBrain owns it.**  
> **Gates are green with quoted evidence — unit and live.**

---

## 9. Subagent template (copy)

```
Wave N agent K (mission: adversarial|delete|split|refactor-layout|proof|fix|live-aspire|docs-honesty)

Vision restatement: <one sentence>

Write scope: <exact paths>
Obey: CLAUDE.md; architecture §4.6; packages.md; scoring rule §1 of prompt-200-grill.md
Protected: <list>
Must-not-return: ProbeHost, UiGateway-in-Kernel, IFlutter god, Behavior theater, Aspire-only Flutter,
  Dart→Orleans, tokens in journals, wholesale app/, mega-files >400 lines without Explicit hold

Actions:
1. Inventory scope against vision + scoring rule + line counts
2. List trash / bad decisions / layout lies with evidence (git/codegraph/compiler/tests/aspire)
3. Fix, delete, or split autonomously in scope (net reduction preferred; every file ≤400 lines)
4. Re-home types into folders/namespaces that match architecture family
5. TDD: red proof first for Built behavior
6. Verify: <commands including aspire when hosting touched>
7. Grill board §2 answers (13 questions) in commit message if committing
8. Foreign dirty tree: leave unstaged

Do not expand scope. Do not invent Behavior rail. Do not claim green without output.
Do not claim hosting Built if Aspire topology is red.
```

---

## 10. Live demo commands (update as you prove; start honest — currently suspected broken)

```
# Domain gate
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"

# Line-count audit (fail if any non-excluded source > 400)
# (orchestrator / agents: PowerShell or rg-based scan)

# Dart / Flutter
$env:Path = "E:\tools\flutter\bin;" + $env:Path
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
# package tests + flutter analyze/test/build windows as applicable

# Aspire product sentence — PRIMARY
aspire stop --apphost hosts/DigitalBrain.AppHost   # if needed
aspire start --project hosts/DigitalBrain.AppHost
aspire ps
# expect Healthy: silo, digitalbrain-ui, digitalbrain-flutter (or honest headless), digitalbrain-mcp
# POST open-scene; SSE / host shows SceneOpened without restart
```

---

## 11. Hard stop scorecard (agent 200)

| Field | Content |
| --- | --- |
| Cycles used | 200 (or less if exhausted early with empty waves — say so) |
| Commits | SHAs + one-line grill |
| Trash deleted | paths / net LOC |
| Files split | before→after line counts; all ≤400 or Explicit holds listed |
| Layout refactors | folders/namespaces moved |
| Bad decisions reversed | list |
| Still Designed | honest list |
| Live demo | exact commands + quoted Healthy resources |
| Gate evidence | build/test exit codes + quoted tail + aspire ps |
| Residual gaps | no silent “almost done” |

---

## 12. Anti-patterns from the previous campaign (do not repeat)

1. Declaring victory on unit gates while the user cannot `aspire start`.  
2. Adding dual L0 pins instead of deleting dual product paths.  
3. Leaving 500-line test contract dumps as “proof quality.”  
4. Parallel agents stomping the same file — enforce non-overlapping write scopes.  
5. Claiming Windows/Auto honesty without re-checking PATH and process health.  
6. Inventing Behavior rail or calendar Time under pressure.  
7. Sweeping foreign dirty trees into “cleanup” commits.  

---

**END — 200 AGENTS — GRILL · TRASH · SPLIT · REFACTOR LAYOUT · LIVE ASPIRE · VISION ALIGNMENT**
