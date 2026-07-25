# 200-grill scorecard (2026-07-25)

Durable record of the 200-agent grill → trash → fix campaign (waves G0–G8, agents 1–200).
Not a task checklist. Claims below were re-checked against tree and docs at close.

**One-sentence vision alignment:** The OS is the brain; Flutter is UI vocabulary projected from
journals; compositions own shell/OS logic until the Behavior rail exists; selecting
`FlutterModule` with host options composes the surface because DigitalBrain owns that sentence.

## Scorecard

| Field | Content |
| --- | --- |
| Cycles used | 200 (8 waves G0–G8 covering agents 1–200). Hard stop at 200 — no agent 201+. |
| Close HEAD | `f9f5ee25` (scorecard) on tip after product work `d3ec36f9` |
| Branch | `agent/digitalbrain-hosting-testing` |
| Baseline (exclusive) | `d9ecee85` (`feat(compositions): shell nav, enrichment surface, AI pane OS apps`) |
| Commits | 40 on `d9ecee85..HEAD` (39 product/honesty + this scorecard) |
| Gate evidence | **Green** (G8 agents 185–192). `dotnet build DigitalBrain.slnx -c Release` → exit 0, 0 warn / 0 err. `dotnet test DigitalBrain.slnx -c Release` → exit 0, **Failed 0 / Passed 226**. `npm --prefix docs test` → 22/22. `npm --prefix docs run build` → green. `dart analyze` wire+flutter clean; wire `dart test` 4/4; `flutter analyze` clean; `flutter test` 21/21. |
| Close tree | Clean of campaign WIP except untracked `prompt-200-grill.md` (orchestrator prompt; leave unstaged). |

## Commits (`d9ecee85..HEAD`)

```
d3ec36f9 fix(hosting): drop Kernel-rooted LLM constraint; pin Contracts/Hosting L0
a68c70c8 test(packages): residual Client/Security/Mcp/Testing/metapackage graph pins
f2fae1fb test(kernel): pin Kernel free of Flutter/UI graph and vocabulary
e74be437 docs(claude): honest Flutter Windows chrome Built in §7 status
61d71492 docs(architecture): mark Windows chrome Built in §4.6 honesty table
1d8a286f test(clients): multi-event SSE→ShellSurfaceController without restart
72c2306c test(compositions): strengthen L0 boundary — transitive contracts + Orleans strip pin
980b204a fix(compositions): strip transitive Orleans global usings for real
173246ae fix(compositions): honest OS-scene vs multi-module L1 split
aa8bbb20 test(ui): L1 IDigitalBrain mutator journals and SSE projects SceneOpened
d90d898f test(hosting): pin OS surface absent without Flutter module / With*
015f974c feat(clients): Windows Flutter chrome projects ShellSurfaceController
c5947fe9 fix(clients): pure SSE→ShellSurfaceController projection for headless host
59b1ec3f fix(clients): pin Flutter host env to DIGITALBRAIN_UI_BASE + DIGITALBRAIN_SHELL
10f36c19 fix(wire): drop narrative comment on golden pin test
06bb90f1 test(g2): L2 silo health without production OS surface
b6a4ba17 test(g2): pin Ui exclusive owner product env only
08e400aa test(g2): pin production AppHost csproj to module hosting only
23987f07 fix(g2): pin silo host purity; PrivateAssets on Host analyzer
2d873a88 fix(flutter-hosting): marker-first Auto probe; honest RequireHost message
f473d166 test(g2): pin Ui exclusive owner product env and OS surface source
9b8a0741 fix(mcp): Chat codecs on Orleans client; pin AsClient WaitFor owner graph
cb4f16f0 fix(g2): companion AppHosts omit OS surface; drop Quickstart storage fat
620614f9 docs(g2): honest §4.6 Auto markers and WaitFor graph
1d705989 grill(ai): drop dual HostName and purpose version tag
64c3ccf7 test(g1): drop dual L0 pins and lying DisplayNames
cd07a6bb refactor(hosts): Mcp uses AddDigitalBrainClient; drop Quickstart package fat
452f9c19 refactor(hosting,client): hide projection surface; one DI client path
2f0c1956 fix(samples): drop dead CA1812; honest compositions description; thin PostAuth
1f0e37ab fix(clients): drop dead wire exports and dual projection path
88bf3bbd fix(flutter-hosting): Auto requires Flutter project markers; exclusive host env
fb95618c docs(honesty): residual Built tiers, Behavior wording, slim durable specs
1b42bc7a fix(compositions): OpenHome is home-only; tighten boundary L0
d2009ef7 fix(clients): honesty no fake Windows chrome, unify SSE parse
79ba08ae fix(flutter-hosting): drop redundant WithReference on host
02d32ffe fix(ui): use AddDigitalBrainClient and SSE no-cache
5e1f2908 docs(packages): honest inventory, Compositions row, metapackage pin
7023ddb2 docs(architecture): honest Built/Designed Flutter and Behaviors surface
064d8396 test(apphost): pin OS surface composition across three AppHosts
```

## Trash deleted / collapsed (summary)

Must-not-return surfaces (verified **absent** from tree at close; see mass-deletion spec):

| Surface | Status at close |
| --- | --- |
| `hosts/DigitalBrain.ProbeHost` | Gone |
| `src/DigitalBrain.DevTools` | Gone |
| `tests/DigitalBrain.Simulations` | Gone |
| ModuleDriver / thick Gherkin Features / public Simulation-Scenario vocabulary | Not reintroduced |
| Public `AddBrain` / storage-profile selection / `WithAzureStorage` | Superseded by `AddDigitalBrain(name)` |
| Public Behavior / `IBehavior` / calendar `IReminder` product API | Not invented |

Campaign net trash (this 39-commit range), not full history:

- Dead client dual-projection path and unused wire exports
- Dual L0 pins / lying DisplayNames
- Dual AI HostName / purpose version tag
- Redundant Flutter host `WithReference`; non-exclusive host env
- Quickstart package/storage fat on companion paths
- Narrative golden-pin comment; dead CA1812 sample noise
- Kernel-rooted LLM hosting constraint that mis-owned module composition

Product module families were **not** deleted (AI, Tasks, Time, Google, Salesforce, Flutter, Quickstart).

## Bad decisions reversed

1. **OS surface as AppHost folklore** → production path is
   `AddModule<FlutterModule>(f => f.WithUiEdge().WithFlutterHost())` with L0 pins that OS resources
   are absent without module/`With*` (`hosts/DigitalBrain.AppHost/AppHost.cs` matches docs).
2. **Dual northbound client wiring** → product path is `AddDigitalBrainClient`; projection surface
   hidden; MCP and Ui use the same DI client path.
3. **Fake or premature Windows chrome claims** → no fake chrome; real Windows projection via
   `ShellSurfaceController` + `lib/main.dart`/`windows/`; docs mark first-vertical Windows chrome
   **Built** (not “almost”).
4. **Compositions honesty** → shell-only vs multi-module vs OS-scene-only split; OpenHome is
   home-only; AccountEnrichmentSurface is **not** the Gmail→Salesforce process.
5. **Compositions package purity** → transitive Orleans global usings stripped; boundary L0
   strengthened (contracts only, no Kernel/Orleans as author surface).
6. **Companion AppHosts leaking OS surface / fat** → Testing/Quickstart companions omit production
   OS surface; silo host purity pinned.
7. **Flutter Auto mode lies** → marker-first Auto probe; honest RequireHost messaging; exclusive
   host env (`DIGITALBRAIN_UI_BASE` + `DIGITALBRAIN_SHELL`).
8. **Docs Built/Designed drift** → architecture §4.6/§5, packages inventory, CLAUDE §7 aligned on
   Flutter first vertical Built, Behavior rail Designed, Countdown-only Time.

## Still Designed (honest list — no silent almost)

| Area | Designed / unbuilt |
| --- | --- |
| Behaviors | Proposal, approval, installation, execution, rollback; no `IBehavior` / runner / public behavior test API |
| Time beyond Countdown | `IReminder`, absolute reminders, recurring interval/calendar, DST records, recurrence library |
| AI orchestration | Supervised Task/`IWorker` path; Sequential / Handoff / Magentic bases; conversation compaction |
| Tasks↔AI bridge | Goal↔messages mapping methods for supervised workers; product workers still throw on Accept/Continue/Cancel |
| Google beyond Gmail read | `ICalendar`; provider-neutral capability-tool seam for models |
| Salesforce | Operation auto-approve classification; parking Task on `OutcomeUncertain` via product producer |
| Flutter beyond first vertical | Full product chrome past key/title shell; multi-principal IdP edge; product journal observation on `IDigitalBrain`; scene descriptor node algebra |
| Memory | Explicitly out of scope — not designed |

**Built (not Designed) — do not re-open as missing:** durable neuron/synapse foundation; owner-scoped
`IDigitalBrain`; module activation catalog; `AddDigitalBrain` AppHost composition; Testing path;
AI direct Respond (Concurrent/GroupChat); Tasks L1 with test worker; `ICountdown`; Gmail read +
Salesforce propose/approve mutation path; Flutter first-five vocabulary + Ui HTTP/SSE + module
hosting + headless Dart host + Windows key/title chrome; sample compositions and AccountEnrichment
process sample.

## Live demo (exact commands)

Production OS surface is composed only when Flutter host options are selected (already true on the
product AppHost):

```powershell
# Repo root. Requires Aspire workload / CLI and durable Azurite profile from AddDigitalBrain.
aspire run --project hosts/DigitalBrain.AppHost
# equivalent:
dotnet run --project hosts/DigitalBrain.AppHost
```

Author-facing shape (matches `docs/architecture.md` §4.6):

```csharp
var brain = builder.AddDigitalBrain("brain");
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
    .WithFlutterHost());
builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);
```

Northbound truth path:

```text
Flutter host  →  hosts/DigitalBrain.Ui (HTTP/SSE)  →  IDigitalBrain  →  silo + FlutterModule journals
```

Proof tiers (not a substitute for root gate):

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
# when clients change and SDK present:
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
# Flutter Windows smoke only when windows/ + SDK ready:
# flutter analyze / flutter test / flutter build windows  (from clients/digitalbrain_flutter)
```

## Residual gaps (no silent almost)

1. **Gate not re-quoted at this close cycle** — do not claim green without orchestrator/human root
   gate output in hand.
2. **Behavior rail unbuilt** — compositions under `samples/DigitalBrain.Compositions` are pre-rail
   logic samples (pull-invoked by tests), not installed Behaviors. Do not invent `IBehavior`.
3. **Calendar Time unbuilt** — only `ICountdown` is product schedule vocabulary (`IReminder` type
   absent; pinned by contracts tests).
4. **Supervised AI/Tasks workers unbuilt** — direct `Respond` exists; Accept/Continue/Cancel throw.
5. **Product chrome beyond key/title** — Windows Material shell projects scene key/title from SSE;
   richer descriptors and multi-window product chrome remain open.
6. **No product journal observation on `IDigitalBrain`** — Ui uses host-private session journal
   poll for SSE; that is edge machinery, not a public observation API.
7. **Model-emitted behavior scripts unmeasured** — load-bearing product assumption still outside
   the built foundation.
8. **Campaign prompt file** — `prompt-200-grill.md` may remain untracked; do not treat it as
   architecture. Prefer this scorecard + `docs/architecture.md` for durable truth.

## Residual table — source physical line budget (L8 cycle 48 proof)

Gate: every product/test `*.cs` / `*.dart` physical line count must be **≤400**. Excludes
`bin` / `obj` / `node_modules` / `.dart_tool` / `build`, platform embedders
(`windows|linux|macos`/`flutter|runner`), and `*.g.cs`. Physical lines =
`File.ReadAllLines(...).Length` (blank lines count).

| Metric | Count | Evidence |
| --- | ---: | --- |
| Files scanned | 306 | Repo-wide `*.cs`+`*.dart` after excludes |
| Files **>400** physical lines | **0** | Must be zero — **green** |
| Files >350 physical lines | 1 | Headroom only; not a fail |
| Max physical lines | 374 | `tests/DigitalBrain.Tests/Hosting/FlutterHostingHostModeContracts.cs` |
| Splits required this cycle | 0 | No write/split work |

### Scan table (top residual heads; all ≤400)

| Physical lines | Path |
| ---: | --- |
| 374 | `tests/DigitalBrain.Tests/Hosting/FlutterHostingHostModeContracts.cs` |
| 333 | `tests/DigitalBrain.Integrations.Tests/McpEdge.cs` |
| 324 | `src/DigitalBrain.Testing/TestBrain.cs` |
| 317 | `tests/DigitalBrain.Tasks.Tests/TaskLifecycle.cs` |
| 312 | `tests/DigitalBrain.Ui.Tests/UiEdgeRoundTrip.cs` |
| 308 | `tests/DigitalBrain.Time.Tests/CountdownRecovery.cs` |
| 298 | `modules/DigitalBrain.Modules.Salesforce/Invoke/Invoke.cs` |
| 290 | `tests/DigitalBrain.Tests/Boundary/AssemblyBoundaryContracts.cs` |
| 261 | `modules/DigitalBrain.Modules.Flutter.Aspire.Hosting/FlutterHostingExtensions.cs` |
| 255 | `clients/digitalbrain_flutter/test/shell_surface_test.dart` |
| 250 | `tests/DigitalBrain.Tests/Boundary/CompositionBoundaryContracts.cs` |
| 247 | `src/DigitalBrain.Testing/Cluster/ControllableTimeProvider.cs` |
| 239 | `clients/digitalbrain_flutter/shell/test/shell_chrome_test.dart` |
| 236 | `tests/DigitalBrain.Time.Tests/CountdownLifecycle.cs` |
| 235 | `samples/DigitalBrain.AccountEnrichment/AccountEnrichment.cs` |

**Verdict (cycle 48):** line-budget residual is empty for the fail gate (`>400` = 0). No file
splits performed. Re-scan before claiming after large test/hosting merges — mid-session WIP on
Flutter/AI trees is present outside this proof.

## Live L8 (cycle 47 — live-aspire)

Mission: quote healthy product topology if already up; restart only if needed; prove
`POST open-scene` + SSE `scene-opened`; no product code.

**Session HEAD at probe:** `8f1936b7` (tree dirty with concurrent WIP — not this agent).
**Restart:** not performed (topology already healthy).

### `aspire doctor` (quoted)

```
Aspire CLI version 13.4.6 (channel: stable)
AppHost version 13.4.6 (hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj)
.NET 11.0.100-preview.6.26359.118 installed (x64)
Docker v29.6.2: running (auto-detected (default)) ← active
HTTPS development certificate is trusted
Summary: 5 passed, 0 warnings, 0 failed
```

### `aspire ps` (quoted)

```
DigitalBrain.TestingAppHost.csproj │ running │ 13.4.6 │ PID 54020 │ CLI -     │ Dashboard -
DigitalBrain.AppHost.csproj        │ running │ 13.4.6 │ PID 56844 │ CLI 61968 │
  Dashboard https://localhost:63047/login?t=92d1c2bb24795b5cc669007a0e81168f
```

Co-running `TestingAppHost` makes non-interactive `aspire describe` / `aspire logs` fail with a
selection prompt. Product proof used Aspire MCP `select_apphost` → product AppHost + direct HTTP.

### Product resource health (Aspire MCP `list_resources`, product AppHost)

| Resource | State | Health | Endpoint |
| --- | --- | --- | --- |
| `silo` | Running | Healthy | `http://localhost:5310` (+ silo/gateway TCP) |
| `digitalbrain-ui` | Running | Healthy | `http://localhost:5080` |
| `digitalbrain-flutter` | Running | Healthy | headless `dart run bin/digitalbrain_host.dart` |
| `digitalbrain-mcp` | Running | Healthy | `http://localhost:5000` |
| `brain-storage` / journal / clustering / reminders | Running | Healthy | Azurite local ports |
| `brain-ai-ollama` + `brain-ai-llama32` | Running | Healthy | `http://localhost:63048` |

### `/health` probes (quoted)

```
digitalbrain-ui:  HTTP 200 body="healthy"
digitalbrain-mcp: HTTP 200 body="healthy"
silo:             HTTP 200 body="healthy"
```

### POST open-scene + SSE scene-opened (quoted)

Shell `live-2e3ff9847254`, Ui base `http://localhost:5080`:

```
POST /shells/live-2e3ff9847254/scenes  body={"sceneKey":"live-home","title":"Live Home Cycle47"}
  -> HTTP 202 Accepted

GET  /shells/live-2e3ff9847254/events?afterSequence=0
  -> HTTP 200 contentType=text/event-stream
  -> data: {"sequence":1,"sceneKey":"live-home","title":"Live Home Cycle47",
            "commandId":"3eb8e1ac031c46448dc934e6fb791c3b",
            "shell":"shell:dev/live-2e3ff9847254"}
     event: scene-opened
```

Headless Flutter host had already projected an earlier live open (console log search
`scene-opened`): `scene-opened seq=1 key=agent9-live title=Agent9 Live`.

### Grill verdict

| Claim | Result |
| --- | --- |
| Product AppHost live without restart | **Pass** |
| silo / ui / flutter / mcp Healthy | **Pass** |
| Northbound POST Accepted | **Pass** (202) |
| SSE projects `scene-opened` with sceneKey/title/sequence | **Pass** |
| Product code changes this cycle | **None** |
| Residual risk | Co-running TestingAppHost confuses CLI resource commands; use MCP select or stop TestingAppHost for clean `aspire logs/describe` |

## Authority

| Doc | Role |
| --- | --- |
| `docs/architecture.md` | Plan of record; Built vs Designed |
| `docs/packages.md` | Package inventory honesty |
| `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md` | Hosting/testing freeze |
| `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md` | Must-not-return cut |
| `CLAUDE.md` | Gates, grilling, agent way of working |

---

## Re-execution campaign (prompt-200-grill supersession — 2026-07-25 session)

Prior G0–G8 left unit gates green while product `aspire start` topology was still unusable.
This re-run treated that as a **failed success criterion**. Orchestrator + subagents executed
waves L0–L8 with non-overlapping write scopes. **~50 real subagent cycles** completed this
session (not vanity-fill to 200); remaining budget reserved for residual only — hard stop
still 200 total if a later wave continues.

**Baseline HEAD at session start:** `ab54d2f9`  
**Mid-session commit:** `8f1936b7` (Google Gmail capability folders)  
**Vision restatement:** A brain programmed in ordinary C# that can program itself; UI is
Flutter vocabulary + compositions over journals — not a second Dart kernel.

### Outcomes (quoted)

| Gate | Result |
| --- | --- |
| Root `dotnet build DigitalBrain.slnx -c Release` | **0 warn / 0 err** (agent 46) |
| Root `dotnet test DigitalBrain.slnx -c Release` | **233 passed / 0 failed** (agent 46) |
| `npm --prefix docs test` / `build` | **22/22** + vitepress green (agent 45) |
| Dart/Flutter clients | analyze clean; wire 4; flutter 18; shell flutter test 4 (agent 49) |
| Physical lines `*.cs`/`*.dart` **>400** | **0** of 306 scanned (agent 48) |
| Live product topology | silo/ui/flutter(headless)/mcp **Healthy**; POST 202 + SSE scene-opened (agents 1,11,21,41,47) |

### Product deltas this re-run (summary)

| Area | Change |
| --- | --- |
| Live Aspire P0 | Ui `WithHttpEndpoint` + health; pure-Dart headless host; `shell/` Windows chrome; `FlutterHostLaunch` shell discovery + CLI probe |
| Ui composition | `MapUiHost` single path; Aspire `AddDigitalBrainClient()` owner default shared with MCP |
| Clients | Root pure Dart; nested `shell/`; SSE fail-closed without explicit `event: scene-opened` |
| Mega-file split | TaskNeuron, Salesforce, Countdown, PackageBoundary, DispatchManifest, soft overs — all ≤400 |
| Layout | AI Clients/LLM/Orchestration; Google Gmail/; Tests Hosting/Boundary/Flutter/Packages |
| Docs honesty | Live residual language; site.test pins pure-Dart + nested shell + not Built-live forever |
| Trash | Session `prompt-200-grill.md` removed; dual client owner resolve collapsed |

### Still residual (honest)

1. Live topology can flap under multi-agent AppHost thrash — **snapshots proven, not forever Built-live**.
2. Behavior rail, calendar Time, supervised AI workers — **Designed / unbuilt**.
3. Windows chrome still key/title first vertical; full product chrome Designed.
4. Co-running TestingAppHost confuses non-interactive `aspire describe`/`logs`.

### Diff grill (orchestrator close)

| Question | Answer |
| --- | --- |
| Added with no consumer? | Nested `shell/` package (Windows chrome consumer); L0 hosting pins; Explicit live Ui fact |
| Claimed without command? | No root/live/docs green without agent-quoted output above |
| Changed that I did not change? | Google agent committed `8f1936b7` mid-session; surfaced |

End of 200-grill scorecard (includes re-execution close).
