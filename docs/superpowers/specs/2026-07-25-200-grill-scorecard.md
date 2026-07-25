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
| Close HEAD | `d3ec36f9` (`fix(hosting): drop Kernel-rooted LLM constraint; pin Contracts/Hosting L0`) |
| Branch | `agent/digitalbrain-hosting-testing` |
| Baseline (exclusive) | `d9ecee85` (`feat(compositions): shell nav, enrichment surface, AI pane OS apps`) |
| Commits | 39 on `d9ecee85..HEAD` (listed below) |
| Gate evidence | Full root gate (`dotnet build` + `dotnet test` on `DigitalBrain.slnx -c Release`, docs npm) is the completion oracle per `CLAUDE.md`. This close cycle is **docs-honesty / scorecard only** and did **not** re-run the root gate. Orchestrator or a follow-on human must quote green gate output before any “gates green” product claim. |
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

## Authority

| Doc | Role |
| --- | --- |
| `docs/architecture.md` | Plan of record; Built vs Designed |
| `docs/packages.md` | Package inventory honesty |
| `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md` | Hosting/testing freeze |
| `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md` | Must-not-return cut |
| `CLAUDE.md` | Gates, grilling, agent way of working |

End of 200-grill scorecard.
