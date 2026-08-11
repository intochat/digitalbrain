# S1.4-GREEN — Execution kernel report

## What changed

| Path | Change |
|------|--------|
| `src/Modules/Tasks/**` | **Deleted** (full module) |
| `src/Modules/Execution/Contracts/**` | New contracts: `IExecution`, `ApplyExecution` (+ `Start`/`Cancel`/`ResolveOperation`), snapshots, blockers, attempt facts, operation ledger, worker + user-action vocabulary; wire aliases `db.execution.*` |
| `src/Modules/Execution/Execution/**` | `ExecutionNeuron` (partials), durable state, dispatch relay, `WorkerNeuron` base, bounded receipts/ops |
| `DigitalBrain.slnx`, Kernel/AppHost csproj, `DigitalBrainComposition.cs` | Point at Execution module |
| `src/Modules/AI/Contracts/IGroupChat.cs`, `GroupChat.cs`, AI.Contracts.csproj | Dropped `IWorker` + dead `SupervisedNotImplemented` throws; removed Tasks project reference |
| `src/Tests/.../BrainClusterFixture.cs` | Compose Execution + `RestartSilosAsync` |
| `src/Tests/.../ExecutionHarnessWorker.cs` | Scriptable test worker (`GrainType("worker")`) |
| `src/Tests/.../ExecutionSpikeProofs.cs` | Spike matrix + manifest alias pin |
| `DigitalBrain.Tests.csproj` | ProjectReference to Execution |

### Production callers of old Tasks module

**None.** Verified by search: only composition (`DigitalBrainComposition`), AppHost/Kernel project refs, and AI `IGroupChat : IWorker` (unimplemented supervised path). No chat/UI/MCP/Salesforce production adapter called `ITask`/`StartTask`. Alias rename was therefore safe (no real data).

## Salvage vs replace

| Area | Decision | Why |
|------|----------|-----|
| Durable execution grain + optimistic revision | **Salvage** | Sound single-writer state machine |
| Idempotent command receipts (`CommandId` → snapshot) | **Salvage** + **deepen** | Bounded to 64 (chat-style `RememberedCommands`) |
| Worker Accept/Continue/Cancel + relay outbox dispatch | **Salvage** | Durable redispatch + one-shot relay remains correct |
| Attempt facts + reminder retry for *retryable failures* | **Salvage** | Kept; still refuses auto-retry when `OutcomeUncertain` |
| Operation ledger phases Prepared→Dispatched→Completed/Uncertain | **Salvage** | Phase machine is sound |
| Sequence+AttemptId operation identity | **Replace** | P0-8: retries re-keyed completed effects. Now attempt-stable `OperationKey` string |
| Unbounded receipts/operations dictionaries | **Replace** | Bounded retention (64 receipts; 64 ops with live rows protected) |
| No reconciliation for `OutcomeUncertain` | **Replace** | `ResolveOperation` Apply command: Completed \| Failed \| PermitRetry |
| Separate `Start`/`Cancel` grain verbs | **Replace** (surface) | Hybrid `Apply`/`Read`; Cancel is an Apply command with `ExpectedRevision` |
| `tasks.*` aliases / `ITask` / `TaskNeuron` / grain `task` | **Replace** | `db.execution.*` / `IExecution` / `ExecutionNeuron` / grain `execution` |
| GroupChat `IWorker` dead throws | **Delete** | S1.5 Conversation adapter is the first production consumer of Execution; GroupChat stays direct `Respond` only |
| Worker dispatch envelope handling | **Deepen** | Public `WorkerNeuron` base maps `DispatchWorker*` → `IWorker` verbs (was missing any worker implementation path) |

## External contract shapes

```csharp
// Grain: execution:{owner}/{name}
// Contract id: db.execution
public partial interface IExecution : INeuron, IHandle<ApplyExecution>
{
    Task<ExecutionSnapshot> Apply(ApplyExecution command);
    Task<ExecutionSnapshot> Read();
}

// db.execution.apply  →  RequestSynapse<ExecutionSnapshot>
public sealed record ApplyExecution(
    CommandId CommandId,
    ExecutionApplyCommand Command,
    long? ExpectedRevision = null);

// Commands (not separately addressable as model tools — nested under Apply):
public sealed record StartExecution(Goal Goal, NeuronId Worker, ExecutionPolicy Policy, NeuronId? RetryOf = null);
public sealed record CancelExecution; // requires ExpectedRevision on Apply
public sealed record ResolveOperation(
    string OperationKey,
    OperationResolution Resolution, // Completed | Failed | PermitRetry
    ProtectedPayloadReference? ResponsePayload = null,
    string? RedactedSummary = null);

public sealed record ExecutionSnapshot(
    Goal Goal, NeuronId Worker, ExecutionPolicy Policy, ExecutionState State,
    long Revision, AttemptId? ActiveAttempt, ExecutionBlocker? Blocker,
    Result? Result, Failure? Failure, IReadOnlyList<FactReference> Evidence,
    NeuronId? RetryOf, int AttemptCount);
```

**Internal** (workers / custody — not the hybrid public surface): `PrepareOperation` / `TransitionOperation` / `ReadOperation`, attempt facts, user-action park/complete/deny, `IWorker` + dispatch envelopes, reminders, operation ledger.

## Tests

| Test | Role |
|------|------|
| `DuplicateStartCommandIdReturnsTheSameExecutionReceipt` | Spike: duplicate submission |
| `ExplicitCancelReachesCancelledThroughTheWorker` | Spike: explicit cancel |
| `OauthStyleBlockerWaitThenResumeCompletes` | Spike: blocker wait + resume (simulated OAuth / InputRequired) |
| `UncertainExternalWriteBlocksWithoutAutoRetryThenResolves` | Spike: OutcomeUncertain + no auto-retry + ResolveOperation |
| `OperationKeyIsAttemptStableAcrossRetryableFailure` | Spike/P0-8: attempt-stable op identity |
| `ExecutionStateSurvivesSiloRestart` | Spike: silo restart durability |
| `ExecutionManifestUsesDbExecutionAliases` | Alias characterization (`db.execution*`) |

No prior RED `PIN-DEFECT(P0-8)` pins existed in `src/Tests` (confirmed). Characterized desired P0-8 behavior as green spike pins rather than defect pins.

## Spike matrix results

| Scenario | Result | Evidence |
|----------|--------|----------|
| Duplicate `CommandId` Start | **PASS** | Same revision/attempt; worker Accept count = 1 |
| Explicit Cancel | **PASS** | Cancelling → Cancelled via worker `AttemptCancelled` |
| Blocker wait + resume (OAuth-style `InputRequired`) | **PASS** | Waiting → resume synapse → Succeeded |
| Uncertain external write, no auto-retry | **PASS** | Stays `OutcomeUncertain` across retry-window delay; AttemptCount stable |
| ResolveOperation reconciliation | **PASS** | Completed resolution → Succeeded |
| Attempt-stable operation key | **PASS** | `stable-write` remains Completed across retryable failure / next attempt |
| Silo restart | **PASS** | Waiting + InputRequired + revision/attempt survive `RestartSiloAsync` |
| Manifest aliases | **PASS** | `db.execution` / `db.execution.apply` / prepare + attempt-outcome-uncertain |

## Gate

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node env noise only: NO_COLOR/FORCE_COLOR — not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 131, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~45s

ExecutionSpikeProofs alone: Total: 7, Failed: 0
```

Zero live `Modules.Tasks` / `DigitalBrain.Tasks` / `ITask` / `TaskNeuron` code references remain under `src/`. Dockerfile historical module-list comment updated away from `TasksModule`.

## Conflicts & risks

1. **No prior P0-8 RED pins** — GREEN characterized and fixed in one session; GRILL should verify no silent residual `tasks.*` outside docs.
2. **Operation request/reply from workers** still uses fire-and-forget `SendAsync` (outbox). Prepare/Transition order relies on outbox ordering; workers do not await replies mid-turn (kernel trap 1). Fine for harness; production workers should follow the same discipline.
3. **`IWorker` remains a public `INeuron` interface** in the contracts assembly, so it appears in the module neuron list (same as pre-rename). Not a ClientEntryPoint.
4. **In-memory reminders** after silo restart: durable grain state survives; reminder re-registration after restart is Orleans reminder-service dependent. Spike only asserts state survival, not reminder re-arm. Restart helper restarts silos one-at-a-time and the restart pin retries `Read` until the client rebinds (avoids dual-gateway blip flakes).
5. **S1.5 dependency**: Conversation adapter must implement a real `IWorker` (or use `WorkerNeuron`) and call `Apply`/`Read` — GroupChat no longer pretends to.

## Out of scope (not touched)

- Conversation / chat wiring (S1.5)
- MCP calls, HTTP host, Flutter
- Behavior Studio
- Docs outside this report (`CONTEXT.md` / `CLAUDE.md` / `INTERCONNECT-REVIEW.md` still mention Tasks historically)
- MAF / Durable Task Scheduler
