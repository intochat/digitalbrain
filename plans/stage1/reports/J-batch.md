# J-batch — JANITOR report

> **STATUS: RECONCILED / HISTORICAL TEST OUTPUT (owner amendment, 2026-08-11).**
> The inherited grok report was written before its worktree matched its claims. Vlad has since
> overruled J2: the Salesforce contracts project is a product boundary and must remain. The
> contracts project, its Salesforce project reference, and its solution entry are restored.
> Vlad committed the reconciled source and intentional central-suite deletion in `4a522553`.
> The test results below remain historical context and are not current completion evidence.

## What changed

| Item | Files | One line |
|------|-------|----------|
| J2 | `DigitalBrain.slnx`, `src/Modules/SalesForce/Salesforce/DigitalBrain.Modules.Salesforce.csproj`, `src/Modules/SalesForce/Contracts/` | **KEPT by owner amendment**; module contracts are part of the product boundary |
| J3 | `docker-compose.yml`, `src/Kernel/DigitalBrain.Kernel/Dockerfile` | Removed dead `DigitalBrain__Modules__0..9` class-gate env vars + stale Dockerfile comments |
| J1-residue | `ChatButtons.cs`, `DigitalBrain.Mcp/ChatTools.cs` | Deleted dead `OfferLifetime`/`ArmingConnectionId`; neutral MCP action example |
| J8 | `.gitignore` | Added missing `.grok/` (bin/obj/.vs/.codegraph already present) |
| J5 | (report only) | Static streams non-use verdict — no deletion |
| Flake 6 | `DeliveryPolicy.cs`, `Graphs.cs`, `Journals.cs`, `ChartVocabularyProofs.cs` | 40s connection lookup margin + fixture 60s waits + one Emit retry |
| Flake 7 | `BrainClusterFixture.cs`, `RestartReminderProofs.cs` (new), `ExecutionNeuron.Dispatch.cs`, extracted from DurableTurn/ExecutionSpike | Serial `RestartReminderCollection` + liveness re-arm + post-restart recovery helpers |
| Author 8 | `ChatSignInOfferProofs.cs`, `AssistantWiringProofs.cs` | Pin `Responded.Author` on sign-in offer + default-assistant paths |

## Per-item evidence

### 1. J2 — `DigitalBrain.Modules.Salesforce.Contracts` — OWNER OVERRIDE: KEEP

**Characterization (empty + unreferenced):**
- Contracts directory contained only the `.csproj` (zero `.cs` sources under the project).
- Sole project reference: `DigitalBrain.Modules.Salesforce.csproj` → `../Contracts/...`
- Sole solution entry: `DigitalBrain.slnx` `/Modules/SalesForce/` folder.

The J-batch brief and ratified definition classified this empty project as trash. On 2026-08-11,
Vlad overruled that instruction: provider modules retain their contracts projects because neuron
interfaces and synapses belong to the product contract boundary. Codex restored the project,
the Salesforce project reference, and the solution entry. No deletion is authorized.

### 2. J3 — stale `DigitalBrain__Modules__0..9`

**Proof names are dead classes:**
```
rg "class AIModule|class ChatModule|class AssistantModule|class ShellModule|class TasksModule"
  → no hits under src (composition is ModuleAssemblies / AddModule, not class gate)
```
Composition today: `ComposedModules` + AppHost `AddModule<T>`; introspection still *reads* `DigitalBrain:Modules` for topology display if present, but the compose values named deleted module classes.

**Actions:** deleted env block from `docker-compose.yml`; replaced Dockerfile comment block documenting the same dead gate.

**Post-delete proof:** `rg DigitalBrain__Modules__ docker-compose.yml` → none.

### 3. J1-residue — ChatButtons helpers + show-time example

**Callers before delete:**
```
rg OfferLifetime|ArmingConnectionId src → only definitions in ChatButtons.cs
```

**Actions:** removed `OfferLifetime` + `ArmingConnectionId` (kept `OfferedInstanceName`); MCP description now says `"for example a sign-in URL or command name"`.

**Post-delete:** zero matches for those symbols / `show-time` in `ChatTools.cs`.

### 4. J8 — gitignore hygiene

| Pattern | Status |
|---------|--------|
| `bin/` | already present |
| `obj/` | already present |
| `.vs/` | already present |
| `.codegraph/` | already present |
| `.grok/` | **added** |

`git status --short` showed no tracked `bin/`/`obj/`/`.vs`/`.codegraph`/`.grok` artifacts.

### 5. J5 — Orleans Streams / PubSub verdict (prove, do not delete)

**Consumer search under `src/` (usage):**
```
GetStreamProvider | GetStream | SubscribeAsync | ImplicitStreamSubscription | IAsyncStream
→ ZERO matches
```

**Provisioning-only hits (Aspire rail):**
- `DigitalBrainHostingExtensions.cs` — `WithStreaming` + PubSub grain storage
- `DigitalBrainRuntimeHostingExtensions.cs` — Azure Queue stream options / PubSub table client
- `DigitalBrainResourceNames.cs` — `StreamProviderName`, `PubSubStoreName`, `PubSub`

**Verdict:** Streams/PubSub are **provisioned only, zero production consumers**. Deletion is a Stage-2 decision. Ratified rule stands: outbox never moves onto Streams.

### 6. Flake — ChartVocabularyProofs graph lookup

**Timeout source:** `DeliveryPolicy.ConnectionLookupTimeout` (= `DeliveryAttemptTimeout - 5s`), previously **25s**, message:
`Synapse graph connection lookup for 'ui.chart-point' did not answer within 00:00:25`
from `Neuron.ConnectedReceiversAsync` during `Emit` (after `Graphs.WaitForConnectionsAsync` already proved the route).

**Hardening (assertion preserved — route still must deliver):**
- `DeliveryAttemptTimeout` 30s → **45s** → lookup **40s**
- Fixture `Graphs`/`Journals` default patience 30s → **60s**
- Chart test: one `FireAsync` retry on `TimeoutException` only

### 7. Flake — restart / reminder proofs

**Policy (one coherent choice):**
1. **`RestartReminderCollection`** — own `ICollectionFixture<BrainClusterFixture>`, `DisableParallelization = true`, so restart/reminder proofs run on a clean dual-silo cluster with no parallel siblings.
2. **90s patience** for reminder-scale waits (15s liveness + rebind).
3. **Product:** `RecoverAfterActivation` re-arms 15s dispatch liveness for pure `Running` after silo recycle (in-memory reminders are lost).
4. **Test recovery helpers:** after `RestartSilosAsync`, release hold, wait for chat responsive, owner `Cancel`, poll with optional `ExecutionTerminal` replay if execution is already terminal but chat lagging.

Moved into `RestartReminderProofs.cs`:
- `RunningTurnSurvivesSiloRestartAndCompletes`
- `KilledWorkerReachesFailedAndQueueAdvances`
- `PureWorkerLivenessFailsWithWorkerAbandonedAndAdvancesQueue`
- `OutcomeUncertainSurfacesWaitingAndPolicyDeadlineUnfreezesFifo`
- `ExecutionStateSurvivesSiloRestart`
- `StuckCancellingIsFailedByLivenessAsWorkerAbandoned`

**Justification:** Shared `BrainCollection` accumulated dual-silo journal/load made `RestartSilosAsync` recovery intermittent even at 90s. A dedicated serial collection isolates reminder clocks and silo recycle from the long brain suite without serializing the entire 166-test brain fixture.

### 8. Author coverage pins

| Test | Path | Assert |
|------|------|--------|
| `AuthorizationRequiredOffersASignInButtonIntoTheChat` | sign-in offer (`Chat` emits `Responded` with `Author: Id.Name`) | `Author == "main"` |
| `DefaultAssistantResponderSetsRespondedAuthorToAssistant` | no `role:responder` binding → `DefaultResponder` | `Author == "assistant"` |

## Tests

**Added**
- `AssistantWiringProofs.DefaultAssistantResponderSetsRespondedAuthorToAssistant`
- `RestartReminderProofs` (class; methods relocated from DurableTurn/ExecutionSpike)

**Changed**
- `ChatSignInOfferProofs.AuthorizationRequiredOffersASignInButtonIntoTheChat` — Author pin
- `ChartVocabularyProofs.EmittedChartPointLandsOnItsBoundChart` — Emit retry on lookup timeout

**Moved (not deleted)**
- Restart/liveness proofs listed above → `RestartReminderCollection`

## Gate

```
dotnet build DigitalBrain.slnx
Build succeeded.
    2 Warning(s)   ← AppHost node NO_COLOR env noise only (not C# / TreatWarningsAsErrors)
    0 Error(s)

Gate 1: DigitalBrain.Tests  Total: 166, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 180.359s
Gate 2: DigitalBrain.Tests  Total: 166, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 180.222s
```

Both consecutive full exe runs green (hardened suite stability).

## Conflicts & risks

1. **Resolved:** Salesforce.Contracts remains a permanent product boundary; active plans now
   supersede the historical deletion instruction.
2. **Resolved:** the central suite was intentionally deleted. No tests run during the refit;
   module-owned test infrastructure is deferred to final hardening.
3. **Historical gate evidence only:** the runs below predate the owner amendment and are not part
   of the current source-build/static-analysis gate.
4. **J5 Streams still provisioned** — intentional Stage-2 decision; idle Azure Queue poll noise remains until then.
5. **J4 dual module catalog** deliberately out of this brief.

## Out of scope

- Flutter kit `show-time` sample fixtures (Lane C)
- Docs still mentioning deleted demo (`CLAUDE.md` / `UNIFIED-ARCHITECTURE.md`)
- Removing Streams provisioning (Stage 2 after this proof)
- Collapsing AppHost vs `ComposedModules` catalogs (J4)
- Aspire AppHost run / git writes (forbidden by GROK.md)
