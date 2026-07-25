# Architecture-aligned mass deletion (2026-07-25)

Direction cut: delete non-product surface, leave a pseudocode-shaped spine.
Build/test may be red. Green is not the success metric.

## Non-negotiable: modules ship

**Do not delete module families.** AI, Tasks, Time, Google, Salesforce, and
Quickstart are out-of-the-box product vocabulary. Packages under `modules/` and
`samples/DigitalBrain.Quickstart*` stay in the solution.

Allowed against modules:

- rewrite or stub shitty *implementation* (orchestration soup, god-files)
- delete tests that only lock trivia about them
- reorganize folders under the module package

Forbidden:

- removing a module package, contracts package, or AppHost `AddModule` path
  because “tests don’t prove it” or “implementation is bad”
- treating an unproven module as trash for deletion of the product surface
- deleting `samples/DigitalBrain.AccountEnrichment` — it is the behavior example that
  shows shipped modules working together (rewrite shitty code; keep the sample)

Authority: `docs/architecture.md`, hosting/testing design 2026-07-24, CLAUDE.md
oracles. When CLAUDE “gates must pass” fights this cut, prefer architecture + this
file and treat red as expected debt until a later restore slice.

## 1. Trash ledger

| Path / type | Why trash | Consumer today? | Action | Risk |
|---|---|---|---|---|
| `hosts/DigitalBrain.ProbeHost` | Raw `IGrainFactory` HTTP probe topology; not author path | Only `TestingAppHost` + `HostedRestart` / Topology L2 | **delete** | L2 restart proof dies until rewritten against Quickstart Host |
| `hosts/DigitalBrain.TestingAppHost` probe resource | Depends on ProbeHost | L2 graph | **collapse** to silo-only (or Quickstart graph) | HostedRestart red |
| `samples/DigitalBrain.AccountEnrichment` | Multi-module **behavior** example (Gmail+Salesforce together) | Host catalog; architecture sample | **keep**; rewrite thin composition; delete trash implementation/tests only | Low if kept as sample |
| `src/DigitalBrain.DevTools` | Orleans dashboard + dev journal helper | Host + ProbeHost Development paths; one L2 dashboard test | **delete** | Lose `/dashboard` in Development |
| `tests/DigitalBrain.Simulations/*` | Orphan generated `.feature.cs` leftovers; no csproj; name is retired Simulation surface | Docs `specification.md` generator only | **delete** | Spec page source gone — mark Designed / remove generator hop |
| `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs` | Repo-wide string scanners, `Simu`+`lation` splits, file shape | L0 only | **delete** | Lose absence locks |
| `tests/DigitalBrain.Tests/ModuleTemplateContracts.cs` | Folder matrices, PackageReference lists, source substrings | L0 only | **delete** | Same |
| `tests/DigitalBrain.Tests/PackableSurfaceContracts.cs` (bulk) + similar string locks | Source/package trivia | L0 | **delete bulk**; keep ≤10 assembly-reflection boundaries | May miss leak regressions |
| Source-shape Time tests (`ArmLocalTimer`, `GetAwaiter` substring) | Implementation trivia | Time L1 source scanner | **delete scanners**; keep behavioral lifecycle/recovery | Dual-timer regression possible — mitigated by Countdown already reminder-primary |
| Public `BrainTestEvent` / `BrainTestFault` / artifact DTO zoo | Diagnostics API wider than product need | Testing internals + failure exceptions | **internalize**; expose fail via exception message/attachment only | TestingTests that assert public DTO shape red |
| `RunningAppHost` (~623 lines) | Half is speculative diagnostics | L2 | **collapse later** to start / resource / wait / restart / dispose | Medium — do after ProbeHost cut |
| `DigitalBrainBuilder.GetApplicationBuilder` / `GetOrAddState` public | EditorBrowsable public for module hosting packages | AI + MCP Aspire.Hosting only | **internal** + `InternalsVisibleTo` module hosting | Low if IVT correct |
| Storage profile / `AddBrain` / `WithAzureStorage` APIs | Already deleted from code; docs still lie | Docs | **fix docs** | Low |
| `ModuleDriverModule` + probe vocabulary (~551 lines) | Second test module runtime parallel to product modules | ModuleTests Gherkin + AI/Google/SF contracts | **delete most**; retain only if a behavioral kernel proof has no Quickstart/Time home | ModuleTests mass-red — acceptable |
| AI orchestration internals (`GroupChat`, `WorkflowRunner`, MAF fingerprint zoo) | Huge surface, sparse real L1 proof | ModuleDriver AI paths, Host catalog | **stub/delete unproven**; keep thin `ILLM` + Llama32/Gpt56 if Host/Mcp need them | Host/MCP may break until smoke restored |
| Google/Salesforce full MCP runtimes | Real modules but debt-heavy | Host AppHost selection | **keep packages** for now (have consumers in AppHost); do not deepen | Medium — leave for later cut |
| Kernel `TimerWork` / module `CreateTimer` grain wakes for schedule | Dual path debt | Countdown is already reminder-primary; ControllableTimeProvider.CreateTimer is **clock** seam not module schedule | **keep** clock timer seam; ensure no Kernel schedule policy for modules | Low |
| `CountdownNeuron` partials | N/A — already single file | Time L1 | **keep** single-file; do not split | — |
| Kernel Neuron partials flat in package root | Human navigation | All | **folder reorg** under `Neuron/`, `Filters/`, `Outbox/`, `Hosting/`, `Serialization/` | Low if namespaces unchanged |
| Behaviors / `IBehavior` / calendar `IReminder` product API / central brain neuron | Unbuilt product; inventing is failure | None | **do not invent** | — |

### Line-count anchors (source, pre-cut)

| Area | ~lines |
|---|---|
| ProbeHost | 76 |
| AccountEnrichment | 340 |
| DevTools | 52 |
| Simulations orphan | ~2400 (generated leftovers) |
| DigitalBrain.Tests L0 | ~5668 |
| ModuleTests | ~2738 |
| Testing package | ~4727 |
| AI module | ~2007 |
| Kernel | ~2364 |
| Generator god-file | 1050 |
| ModuleTemplateContracts alone | 1016 |
| ArchitectureCutContracts | 382 |
| AppHostTestArtifact | 647 |
| RunningAppHost | 623 |
| ModuleDriverModule | 551 |

Target: ≥50% cut in Testing diagnostics + L0 contracts + ProbeHost + ModuleDriver; material repo shrink overall.

### Autonomous grill cycles (post-protect modules/AccountEnrichment)

| Cycle | Action | Net effect |
|---|---|---|
| L0 purge | Delete ArchitectureCut/ModuleTemplate + bulk AI/serialization/capability L0 | Tests ~3590→~900 |
| TestingTests purge | Delete artifact/edge/clock/journal meta-tests; keep fixture lease only | ~1750→~75 |
| HostTests purge | Delete artifact zoo + ProductionAppHost proofs; keep silo health + exclusivity | ~525→~110 |
| L2 diagnostics | Delete AppHost evidence DTO zoo; thin RunningAppHost to Aspire API | 623→218; 647 deleted |
| Fixtures | Delete WrongModule.Contracts leaf-guard fixture | gone |
| L1 diagnostics | Delete BrainTestArtifact JSON zoo; thin `BrainTestDiagnostics` to wrap failures | 424+ → ~62 |
| AssemblyBoundary | Drop type-closure MAF scanner; keep assembly graph facts | 215 → ~100 |
| AI supervised path | Delete WorkflowRunner/OrleansCheckpointStore/AIWorkerState; thin GroupChat | AI module ~2000 → ~870; IWorker methods throw |
| Tasks dual dispatch | Remove grain-timer continuation; reminder + immediate TryDispatch only | Dual timer/reminder path gone |
| Time L0 | Drop wire-shape scanners; keep vocabulary + method naming | ~254 → ~70 |

## 2. Delete PR plan (ordered)

1. Trash ledger (this file) — done before code.
2. Delete `hosts/DigitalBrain.ProbeHost`; strip probe from TestingAppHost; drop HostedRestart/Topology probe assertions or delete those tests.
3. Delete `samples/DigitalBrain.AccountEnrichment`; drop Host project ref.
4. Delete `src/DigitalBrain.DevTools`; strip Host Program/csproj + any packable lists.
5. Delete `tests/DigitalBrain.Simulations/` orphan tree; stop docs generator hop if it breaks.
6. Delete L0 string scanners: ArchitectureCutContracts, ModuleTemplateContracts, and other pure File.ReadAllText/package-matrix tests.
7. Delete Time source-shape scanners; keep behavioral Countdown tests.
8. Collapse ModuleDriver / ModuleTests probe surface (delete or minimal stub).
9. Internalize test artifact DTOs; narrow public Testing surface.
10. `GetApplicationBuilder` / `GetOrAddState` → internal + InternalsVisibleTo.
11. Kernel folder reorg (Neuron/, Filters/, Outbox/, Hosting/, Serialization/).
12. Optional AI stub: keep contracts + thin LLM path; delete unproven orchestration files if no behavioral consumer after ModuleDriver cut.
13. Docs: architecture hosting snippets match `AddDigitalBrain`; mark unbuilt Designed.
14. Optional later: restore one green Quickstart L1 vertical — not required for direction complete.

## 3. Target folder tree

```text
src/
  DigitalBrain.Abstractions/
  DigitalBrain.Kernel/
    Neuron/
      Neuron.cs
      Neuron.Lifecycle.cs
      Neuron.Journals.cs
      Neuron.Messaging.cs
      Neuron.Capability.cs
      Neuron.Outbox.cs
      Neuron.Concurrency.cs
      Neuron.Turns.cs
    Outbox/
    Filters/
    Hosting/          # DigitalBrainRuntime, JournalStorageHosting, silo extensions
    Serialization/    # JournalJsonContext, wiring helpers as needed
  DigitalBrain.Client/
  DigitalBrain.Aspire/
  DigitalBrain.Aspire.Hosting/
  DigitalBrain.Testing/   # L1+L2 product only; diagnostics internal
  DigitalBrain.SourceGeneration/
  DigitalBrain.Security/
  DigitalBrain.Integrations.Mcp/
  DigitalBrain.Integrations.Mcp.Aspire.Hosting/

modules/   # package names may say Modules/Contracts; public namespaces must not
  DigitalBrain.Modules.<Name>.Contracts/
  DigitalBrain.Modules.<Name>/
    Runtime/Neurons/   # preferred; migrate when touching
    Runtime/Hosting/
  DigitalBrain.Modules.<Name>.Aspire.Hosting/  # only if required

samples/
  DigitalBrain.Quickstart.Contracts/
  DigitalBrain.Quickstart/

hosts/
  DigitalBrain.AppHost/
  DigitalBrain.Host/
  DigitalBrain.Mcp/
  DigitalBrain.TestingAppHost/      # silo-only or Quickstart graph — no ProbeHost
  DigitalBrain.Quickstart.AppHost/
  DigitalBrain.Quickstart.Host/

tests/
  DigitalBrain.Tests/           # ≤10 boundary L0 tests
  DigitalBrain.TestingTests/    # fixture lifecycle L1 product
  DigitalBrain.ModuleTests/     # module L1 — shrink toward product modules
  DigitalBrain.Time.Tests/
  DigitalBrain.Quickstart.Tests/
  DigitalBrain.HostTests/       # L2 exclusive graph
```

Namespaces stay vocabulary-first (`DigitalBrain.Kernel`, `DigitalBrain.AI.*`, …). Folders are for humans.

## 4. Pseudocode vertical (public shape)

```csharp
// AppHost — only this shape
var brain = builder.AddDigitalBrain("brain");
brain.AddModule<QuickstartModule>();
// brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>()); // only while AI package kept
builder.AddProject<Projects.Silo>("silo").WithReference(brain);

// Silo — boring
silo.AddDigitalBrain(); // generated catalog
silo.AddDigitalBrainJournalStorage(config);

// Client — DI only in product path
IDigitalBrain client = ...;
var greeter = client.Get<IGreeter>("welcome");
await client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));

// L1
await using var test = await fixture.CreateBrainAsync(ct);
var g = test.Neuron<IGreeter>();
await test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));
var fact = await g.Outgoing.NextAsync<Greeted>(ct);
await g.RestartHostAsync(ct);

// Time — reminder-primary only
var c = test.Client.Get<ICountdown>("t1");
await c.Start(new StartCountdown(CommandId.New(), TimeSpan.FromHours(1), dest));
await test.Clock.AdvanceAsync(TimeSpan.FromHours(1), ct);
// CountdownElapsed fires via reminder path only
```

No public Simulation/Scenario/AddBrain/storage profile. No second probe host.
`Connect(IGrainFactory)` may remain temporarily for Testing/Aspire DI wiring but is not the author story.

## 5. Expected red debt (honest) — post-cut status

### Verified green (spot-check, not full root gate)

| Surface | Result |
|---|---|
| `dotnet build DigitalBrain.slnx -c Release` | **green** |
| Quickstart L1 | 1/1 pass |
| Time L1 | 17/17 pass |
| Module AI smoke | 1/1 pass |

### Still debt / intentionally thinner

- L2 restart/topology via ProbeHost: **deleted** (HostedRestart, Topology gone); L2 retains silo health + exclusivity proofs only until rewrite against Quickstart Host
- L0 ArchitectureCut / ModuleTemplate / PackableSurface scanners: **deleted**
- ModuleDriver + Gherkin Features + Google/Salesforce/Tasks module L1 via driver: **deleted**
- `BrainTestEvent` / `BrainTestFault` / AppHost evidence DTOs: **internal** (fail via exception + JSON)
- `GetApplicationBuilder` / `GetOrAddState`: **internal** + InternalsVisibleTo module hosting
- Full root `dotnet test DigitalBrain.slnx` may still fail on remaining L0 assembly/graph tests that load `DigitalBrain.Testing` or scan `.worktrees` twins — treat as cleanup, not product regression
- AI orchestration surface (GroupChat/MAF) still in tree; only LLM smoke is proven
- RunningAppHost still large (~623 lines) — collapse deferred
- Generator still one god-file (~1050 lines) — not split this cut

Not expected to invent Behaviors, IReminder product API, or central DigitalBrain neuron to make green.

## 6. Tension with CLAUDE.md

CLAUDE root gate requires green `dotnet build` + full `dotnet test`. This cut
explicitly allows red. Record: **architecture + delete mandate win for this
branch direction**; restore gate only after skeleton matches pseudocode and
one Quickstart/Time vertical is re-proven.
