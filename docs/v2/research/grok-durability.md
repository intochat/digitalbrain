# Grok planning session: durability

Independent planning input, not the final implementation contract. See ../PLAN.md for reconciled decisions. Generated from bounded source excerpts after the first read-only CLI runs ended without usable results.

# Durable composed behaviors — implementation plan

Focus: **durability, accepted work, recovery, owner leases, versioned start/stop, uncertain external actions**. Composition/UI/Twitter examples are out of scope except where they force a durable identity.

DigitalBrain already has the substrate. The work is a **behavior façade + ownership + processor grains** on top of journals, snapshots, command dedup, announcement outbox, pending work, and reminders. Do not add a second outbox, a second command log, or NewPM’s in-memory host.

---

## 1. Existing seams to reuse (do not duplicate)

| Concern | Existing mechanism | File |
|---|---|---|
| Snapshot + outbox | `Neuron<TState>`, `SnapshotEnvelope`, `Announce` / `SaveAsync` / `DrainAnnouncementsAsync` | `src/Kernel/DigitalBrain/Neuron/NeuronOfState.cs` |
| Signal delivery, synapses, cancel, journals | `INeuron` (`Fire`, `Connect`, `Disconnect`, `Deliver`, `CancelReaction`, reads) | `src/Kernel/DigitalBrain.Contracts/Neurons/INeuron.cs` |
| Idempotent typed RPC | `CommandExecution`: Attempted → terminal; replay; `CommandOutcomeUnknownException` on crash mid-attempt | `src/Kernel/DigitalBrain/Commands/CommandExecution.cs` |
| Command → same-neuron work | `CommandReaction.ScheduledWork` → `AdmitTurnWork` / `WakeTurnWorkAsync` (reminders) | `CommandExecution.cs` |
| Typed invoke + auto `CommandId` | `INeuronInvoker` / `DescriptorTable` | `src/Kernel/DigitalBrain/Descriptors/NeuronInvoker.cs` |
| Agent tools (preserve) | `BrainTools` fire/connect/call/describe | `src/Modules/AI/AI/BrainTools.cs` |
| In-graph recipes (preserve) | `RecipeNeuron` + `Instruct` | `src/Kernel/DigitalBrain/Neuron/RecipeNeuron.cs` |
| Crash/restart tests | `BrainSimulation` + `PersistenceDirectory` + `RestartSiloAsync` | `src/Testing/DigitalBrain.Testing/BrainSimulation.cs` |

### Hard kernel rules that the design must obey

1. **`SaveAsync` and `Announce` are illegal inside a command** (`ExecutingCommand` throws). Snapshot writes and outbound signals happen in **reactions**.
2. **Commands persist Attempted before the delegate runs.** A crash after Attempted and before the terminal record is **unknown**, not retry-as-new. Callers must not re-issue a different body under the same `CommandId`.
3. **Failed snapshot write re-reads state** so `AppliedBy` cannot skip a reaction that never committed. Processors must go through `SaveAsync`, not ad-hoc `IPersistentState` writes.
4. **A neuron cannot announce to itself.** Lifecycle work on the behavior grain is **scheduled pending signals**, not self-`Announce`.
5. **`INeuron.Connect` / `Disconnect` have no owner today.** Manual and recipe edges must keep working. Ownership is an **additive interface**, not a signature change on `INeuron`.
6. **`RecipeNeuron` mints `$commandId` with `Guid.NewGuid()`** — not retry-safe. Action processors must **not** copy that. Use a deterministic `CommandId` from stable action identity.

### Accepted-work pattern (the one lifecycle shape)

Start/stop/save **accept work** in the command journal, then **perform effects in reactions**.

```
command (IBehavior.Start)
  → capacity/dedup checks
  → persist Attempted
  → schedule Lifecycle signal on this grain (CommandReaction.ScheduledWork)
  → persist Completed { accepted, behaviorId, revision, desired }
  → WakeTurnWorkAsync (existing reminder)

reaction (ReceiveAsync Lifecycle)
  → SaveAsync(desired lifecycle + ownership map)     // snapshot
  → configure owned processors, ConnectOwned leases
  → Announce/Fire only from this reaction
  → SaveAsync(actual == desired) or SaveAsync(paused/failed-visible)
```

Crash after command, before reaction: **pending + reminder** recover.  
Crash mid-reaction: **snapshot `AppliedBy` + announcement outbox** recover.  
Do not make Start’s command delegate call `Connect` / `InvokeAsync` / `SaveAsync`.

---

## 2. Compatibility constraints

- Branch `v2` on current `feat/standing-composition`. Keep `RecipeNeuron`, `NeuronInvoker`, existing composition tests.
- Do **not** change `INeuron` aliases, `SnapshotEnvelope` field ids (`db.v3.snapshot\`1`), command phases, or `BrainTools` tool names.
- Do **not** import NewPM hosting. Do **not** silently rewrite historical `Instruct` recipes into behaviors.
- Additive Orleans members only: new `[Id(n)]` at the end of existing serializable types; new grain types; new descriptor interfaces.
- `Connect`/`Disconnect` without an owner token stay **unowned** (agent/recipe/manual). Behavior stop may remove **only edges it leased**.
- Shared nodes keep their original `NeuronId` and existing configuration. Behavior may attach leases, never retarget identity, never `SaveAsync` on a shared grain’s domain state.
- Owned processor ids are **deterministic** from `(behaviorId, revision, role)` so recovery reconnects the same grains.
- Solution build: `CodeGraphRefresh=false`. No production DB migration.

---

## 3. Contract sketches

### 3.1 Definition (declarative, persisted, no codegen)

New files:

- `src/Kernel/DigitalBrain.Contracts/Behaviors/BehaviorDefinition.cs`
- `src/Kernel/DigitalBrain.Contracts/Behaviors/IBehavior.cs`
- `src/Kernel/DigitalBrain.Contracts/Behaviors/ICapabilityRegistry.cs`
- `src/Kernel/DigitalBrain.Contracts/Synapses/IOwnedSynapses.cs`

```csharp
[GenerateSerializer, Alias("db.v2.behavior-def")]
public sealed record BehaviorDefinition(
    [property: Id(0)] string BehaviorId,          // stable name, not a grain key by itself
    [property: Id(1)] int Revision,               // assigned by Save; client may send 0
    [property: Id(2)] IReadOnlyList<BehaviorNode> Nodes,
    [property: Id(3)] IReadOnlyList<BehaviorEdge> Edges);

[GenerateSerializer, Alias("db.v2.behavior-node")]
public sealed record BehaviorNode(
    [property: Id(0)] string Role,                // unique in this definition
    [property: Id(1)] string CapabilityId,        // registry key
    [property: Id(2)] string? ResourceId,         // shared: existing neuron; owned: null
    [property: Id(3)] JsonElement Config,         // capability-specific, schema-checked
    [property: Id(4)] JsonElement? InputContract, // optional override; default from capability
    [property: Id(5)] JsonElement? OutputContract);

[GenerateSerializer, Alias("db.v2.behavior-edge")]
public sealed record BehaviorEdge(
    [property: Id(0)] string FromRole,
    [property: Id(1)] string ToRole,
    [property: Id(2)] string SignalType,          // existing signal-type rules (letters)
    [property: Id(3)] JsonElement? PayloadContract);

[GenerateSerializer, Alias("db.v2.capability")]
public sealed record CapabilityDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] string GrainType,           // Orleans grain type for owned processors
    [property: Id(2)] CapabilityOwnership Ownership, // Owned | Shared
    [property: Id(3)] JsonElement ConfigSchema,
    [property: Id(4)] JsonElement InputSchema,
    [property: Id(5)] JsonElement OutputSchema,
    [property: Id(6)] bool HasUncertainExternalEffect);

public enum CapabilityOwnership { Owned = 0, Shared = 1 }

public enum BehaviorLifecycle { Inactive = 0, Starting = 1, Running = 2, Stopping = 3, Stopped = 4, Paused = 5 }
```

Identity rules:

- Behavior grain: `[GrainType("behavior")]`, key = `BehaviorId` (same `NeuronId` rules as today).
- Owned processor: `NeuronId` = `{grainType}:{behaviorId}.{revision}.{role}` — parseable, stable across restart.
- Shared resource: `ResourceId` as given; must already be a valid `NeuronId`.
- Lease id: deterministic `Guid` from UTF-8 `"lease|{behaviorId}|{revision}|{fromId}|{toId}|{signalType}"` SHA-256, first 16 bytes. Same start retries the same lease.

### 3.2 Behavior commands (describe/call, not new agent tools)

```csharp
[Alias("db.v2.behavior")]
public interface IBehavior
{
    Task<SavedBehavior> Save(SaveBehavior command);
    Task<BehaviorValidation> Validate(ValidateBehavior command);
    Task<BehaviorAcceptance> Start(StartBehavior command);
    Task<BehaviorAcceptance> Stop(StopBehavior command);
    Task<BehaviorResolution> Resolve(ResolveBehavior command); // inspect paused unknown work
    [ReadOnly] Task<BehaviorView> Read();
}

public sealed record SaveBehavior(CommandId Id, BehaviorDefinition Definition) : Command(Id);
public sealed record ValidateBehavior(CommandId Id, int? Revision) : Command(Id);
public sealed record StartBehavior(CommandId Id, int Revision) : Command(Id);
public sealed record StopBehavior(CommandId Id, int Revision) : Command(Id);
public sealed record ResolveBehavior(CommandId Id, string StableActionId, ResolutionKind Kind, string? Note) : Command(Id);

public sealed record BehaviorAcceptance(
    string BehaviorId, int Revision, BehaviorLifecycle Desired, SignalId LifecycleWork);

public sealed record BehaviorValidation(
    bool Ok, IReadOnlyList<string> Errors, IReadOnlyList<string> Collisions);

public sealed record BehaviorView(
    BehaviorDefinition Definition, BehaviorLifecycle Desired, BehaviorLifecycle Actual,
    IReadOnlyList<OwnedLease> Leases, IReadOnlyList<PausedAction> Paused);
```

`Save` / `Start` / `Stop` / `Resolve` go through `CommandExecution` (existing `[Command]` descriptor registration). `Read` is `[ReadOnly]` and does not use the command journal.

### 3.3 Snapshot on the behavior grain

```csharp
[GenerateSerializer, Alias("db.v2.behavior-state")]
public sealed record BehaviorSnapshot(
    [property: Id(0)] BehaviorDefinition? Definition,
    [property: Id(1)] BehaviorLifecycle Desired,
    [property: Id(2)] BehaviorLifecycle Actual,
    [property: Id(3)] int ActiveRevision,          // 0 if none
    [property: Id(4)] IReadOnlyList<OwnedLease> Leases,
    [property: Id(5)] IReadOnlyList<PausedAction> Paused,
    [property: Id(6)] string? LifecycleError);

[GenerateSerializer, Alias("db.v2.owned-lease")]
public sealed record OwnedLease(
    [property: Id(0)] Guid LeaseId,
    [property: Id(1)] string FromNeuron,           // concrete id, not role
    [property: Id(2)] string ToNeuron,
    [property: Id(3)] string SignalType,
    [property: Id(4)] int Revision,
    [property: Id(5)] string FromRole,
    [property: Id(6)] string ToRole);

[GenerateSerializer, Alias("db.v2.paused-action")]
public sealed record PausedAction(
    [property: Id(0)] string StableActionId,
    [property: Id(1)] string Role,
    [property: Id(2)] SignalId Trigger,
    [property: Id(3)] CommandId AttemptedCommand,
    [property: Id(4)] string Reason);              // "unknown-outcome" | "not-retryable"
```

### 3.4 Owned synapses (minimal kernel addition)

Do **not** change `INeuron.Connect` / `Disconnect`. Add a descriptor interface on the **source** neuron:

```csharp
[Alias("db.v2.owned-synapses")]
public interface IOwnedSynapses
{
    Task ConnectOwned(ConnectOwned command);       // Command
    Task DisconnectOwned(DisconnectOwned command); // Command
}

public sealed record ConnectOwned(
    CommandId Id, NeuronId Target, string SignalType, Guid LeaseId, NeuronId Owner) : Command(Id);

public sealed record DisconnectOwned(
    CommandId Id, NeuronId Target, string SignalType, Guid LeaseId, NeuronId Owner) : Command(Id);
```

Enforcement (in the existing synapse write path, next to current `Connect`/`Disconnect`):

| Call | Existing unowned edge | Edge leased by this owner | Edge leased by other owner |
|---|---|---|---|
| `Connect` | idempotent, stays unowned | **reject** (do not strip lease) | **reject** |
| `Disconnect` | remove (manual/recipe) | **reject** | **reject** |
| `ConnectOwned` | **reject** (do not capture manual edges) | idempotent if same `LeaseId` | **reject** |
| `DisconnectOwned` | reject | remove if `LeaseId`+`Owner` match | **reject** |

Storage: extend the existing `Synapse` serializable with optional `[property: Id(n)] Guid? LeaseId` and `NeuronId? Owner` **only if** the current `Synapse` type has spare ids and the serializer is additive. If `Synapse` field layout is frozen, store leases in a **parallel list on the same neuron grain state** keyed by `(target, signalType)` and check it in both paths. Prefer the parallel list if there is any doubt about `Synapse` wire compatibility.

`ConnectOwned` / `DisconnectOwned` are commands so retries are deduped with deterministic `CommandId` = hash of `(owner, leaseId, op)`.

### 3.5 Capability registry

In-process table, registered at silo start with modules (same place `DescriptorTable` is built — `DigitalBrainRuntime.Add`):

```csharp
public interface ICapabilityRegistry
{
    CapabilityDescriptor Get(string capabilityId);
    IReadOnlyList<CapabilityDescriptor> All();
    NeuronId OwnedProcessorId(string behaviorId, int revision, string role, CapabilityDescriptor cap);
    void ValidateDefinition(BehaviorDefinition def);          // schemas, ownership, unique roles
    void ValidateConnections(BehaviorDefinition def);         // edge payload vs node in/out
}
```

Shared capabilities **must** supply `ResourceId`. Owned capabilities **must not** (id is assigned). Unknown `CapabilityId` fails validate, never start.

### 3.6 Processor grains (owned, snapshot + Announce)

All owned processors are `Neuron<TState>` subclasses. They **never** expose agent tools.

| Capability | Grain type | Snapshot | Reaction |
|---|---|---|---|
| `filter` | `filter` | compiled predicate config + last admitted `SignalId` | if body matches contract+predicate, `Announce` original or projected signal downstream |
| `map` | `map` | mapper config | validate in, project, validate out, `Announce` |
| `action` | `action` | config + last `StableActionId` + pause flag | `INeuronInvoker.InvokeAsync` with **deterministic** `CommandId`; on `CommandOutcomeUnknownException` / non-retryable failure: `SaveAsync` paused, `Announce` `Paused` (do not retry invoke) |
| `decide` | `decide` | output JSON schema + last decision | model call with **no tools**; validate output against schema; `Announce` `Decision` body. Refuse if the model attempts a tool call |

Shared sources (Twitter receipt, existing modules) stay as they are. Behavior only leases their outbound edges.

**Stable action id** (required for uncertain effects):

```
stableActionId = config.ActionIdTemplate
  .Replace("$behavior", behaviorId)
  .Replace("$revision", revision)
  .Replace("$role", role)
  .Replace("$trigger", delivery.SignalId.ToString())
  .Replace("$key", payloadKey)   // e.g. tweet id, already in the body
```

`CommandId` for the typed invoke = `CommandId` parsed from SHA-256 of `stableActionId` (same bytes every retry). Pass it in the args object so `NeuronInvoker.WithCommandId` does **not** mint a random one.

When the target command is still `Attempted`, invoker/`CommandExecution` throws `CommandOutcomeUnknownException`. Action processor **pauses** and records `PausedAction`. Resume only via `IBehavior.Resolve` (and the action’s own follow-up reaction), never by blindly re-invoking.

---

## 4. Numbered implementation steps

### Step 1 — Contracts and registry only (no runtime effects)

1. Add the serializable types in `src/Kernel/DigitalBrain.Contracts/Behaviors/`.
2. Add `ICapabilityRegistry` + default implementation next to `DescriptorTable` (likely `src/Kernel/DigitalBrain/Descriptors/CapabilityRegistry.cs`).
3. Register `filter`, `map`, `action`, `decide` descriptors (grain type, schemas, ownership). Shared module capabilities (existing counters, ingress) register as `Shared`.
4. Schema check: JSON Schema already used by `MethodDescriptor` / `ArgumentsJson`. Reuse the same serializer options. Conservative: **additionalProperties=false** on node config and edge payload; fail closed.
5. Unit tests: valid graph, missing capability, shared node without `ResourceId`, owned node with `ResourceId`, incompatible edge schemas, duplicate roles, illegal signal types.

No grains yet. No persistence.

### Step 2 — Owned synapse commands on the existing neuron

1. Locate synapse mutate path used by `INeuron.Connect` / `Disconnect` (same assembly as `Neuron` base, not in the excerpts — extend that method, do not fork).
2. Add `IOwnedSynapses` on the neuron grain; register methods in `DescriptorTable` so `describe`/`call` work.
3. Parallel lease map **or** additive synapse fields; persist with the existing neuron `PersistAsync` / journal+state write the Connect path already uses.
4. Tests (file-backed `BrainSimulation`):
   - Manual `Connect` then behavior `ConnectOwned` → reject.
   - Two behaviors, same `(from,to,type)` → second `ConnectOwned` reject.
   - `Disconnect` on leased edge → reject; `DisconnectOwned` with wrong lease → reject; correct lease → remove; second stop idempotent.
   - Unowned `Disconnect` still removes recipe/agent edges.
   - Restart silo; lease still enforced.

### Step 3 — `BehaviorNeuron` as `Neuron<BehaviorSnapshot>`

File: `src/Kernel/DigitalBrain/Neuron/BehaviorNeuron.cs`  
Grain type `"behavior"`. Redeclare `[PersistentState("state", DigitalBrainNames.DefaultGrainStorage)]` on the constructor as `Neuron<TState>` requires.

**Save command (accepted work):**

- Validate structurally (registry, unique roles, contracts) **before** scheduling. Invalid → `CommandRejectedException` (permanent, no resources touched).
- Reject Save that changes a **Running/Starting** revision in place. New revision is stored as the next definition; `ActiveRevision` unchanged until Stop+Start.
- Schedule signal type `Lifecycle` body `{ "op":"save", "revision":N }`.
- Complete with the assigned revision.
- Reaction: `SaveAsync(snapshot with Definition, Desired=Inactive if first save)`.

**Validate command:**

- Pure. Reads stored definition (or args). No schedule. Returns `BehaviorValidation` including **live** `ReadSynapses` collision checks (unowned/foreign leases). Safe to call anytime.

**Start command:**

- Reject if no definition, revision mismatch, or `Desired` is `Starting`/`Running` for a **different** revision.
- Same revision + already `Desired=Running` → replay Completed (idempotent accept).
- Validate **connected schemas and leases** first (call the same logic as Validate). Any error → reject, no schedule.
- Set scheduled work `Lifecycle { op:start, revision }`. Command result `Desired=Starting`. Do not Connect here.

**Start reaction (the only place effects happen):**

1. `SaveAsync(Desired=Starting, Actual=Starting)` first (desired durable before remote calls).
2. For each **owned** node: grain already exists by name; send a `Configure` signal (not a command) with config JSON. Processor reaction `SaveAsync`s config. Order: **processors before opening upstream**.
3. For each edge: `ConnectOwned` with deterministic lease `CommandId`. Shared `from` keeps original id. Record lease in snapshot after each successful owned-connect (`SaveAsync` so a crash resumes remaining edges).
4. `SaveAsync(Desired=Running, Actual=Running)`.
5. If a remote call throws `CommandOutcomeUnknownException` on `ConnectOwned`: persist `Actual=Starting`, `LifecycleError`, **do not retry that connect**; wait for recovery/reconcile (ConnectOwned Attempted is recovered by the **source** neuron’s command recovery, then this behavior’s reminder re-enters start and the ConnectOwned **replay** is idempotent). If the unknown command is an **external action**, that is processor-side (Step 5), not start.

**Stop command / reaction:**

- Accept `Desired=Stopping` even if already `Stopped` (idempotent).
- Reaction: `SaveAsync(Desired=Stopping)` first.
- `DisconnectOwned` **only** `snapshot.Leases` for this `ActiveRevision`. Do not `Disconnect` anything else. Do not delete shared neurons. Do not delete owned processor grain state (history stays); processors **reject new work** when config.lifecycle is stopped (a `Configure {enabled:false}` signal).
- Clear leases from snapshot; `SaveAsync(Desired=Stopped, Actual=Stopped, ActiveRevision=0)`.

**Recovery (no new reminder type):**

- Behavior uses the existing retry reminder because start/stop are pending `Lifecycle` signals and announcement drains.
- On `ReceiveAsync` after restart: if `Desired != Actual`, treat as a synthetic reconcile — same start/stop reaction bodies, same ids. Gate with `IsAppliedBy(delivery)` via the envelope as today.
- Do **not** scan the cluster for orphan processors. Identity is deterministic; unused owned grains stay inert (`enabled:false` or never configured).

**Version pin:**

- `Start(Revision=N)` while definition `N+1` is saved: allowed only if `Actual` is `Stopped`/`Inactive`. Running `N` is unaffected by saving `N+1`.
- Starting `N` while `N+1` exists: uses the definition blob stored **for that revision**. Keep `Definition` as the latest save, plus `ActiveRevision` and a `History` list of prior definitions **or** store only latest + active copy:

  Recommended: snapshot holds `Latest` and `Active` (two `BehaviorDefinition?`). Save writes `Latest`. Start copies `Latest`→`Active` only if revisions match the command. Avoid unbounded history in the snapshot.

### Step 4 — Lifecycle signal vocabulary

Use existing signal rules (letters-only types):

- `Lifecycle` — behavior internal (scheduled, not announced on synapses)
- `Configure` — behavior → owned processor
- `Paused` / `Decision` — processor → downstream / behavior
- Downstream payload types come from the definition edges (`Note`, `Confirmed`, …)

Do not reuse `Instruct` (recipes). Do not put behavior graphs in `RecipeNeuron`.

### Step 5 — Action uncertainty (pause, don’t retry)

In `ActionNeuron.ReceiveAsync`:

1. Compute `stableActionId` and `CommandId` as in §3.6.
2. If snapshot already completed that `stableActionId`, skip (idempotent).
3. If snapshot has it paused, skip (wait for `Resolve`).
4. `InvokeAsync(...)`. Catch:
   - `CommandOutcomeUnknownException` → `SaveAsync(paused)`, `Announce(Paused)`, stop.
   - `CommandFailedException` → if capability `HasUncertainExternalEffect`, treat as pause (may have occurred). If the method is a known idempotent read, surface failed and do not pause.
   - `CommandRejectedException` → not an external effect; fail the reaction (kernel will not mark applied if `SaveAsync` wasn’t called — keep that: **only `SaveAsync` after a known outcome**).
5. Success: `SaveAsync(lastActionId)`, `Announce` result signal.

`IBehavior.Resolve`:

- Command schedules `Lifecycle { op:resolve, stableActionId, kind:confirm|abort }`.
- Reaction: confirm → mark action completed without re-invoke; abort → mark aborted; both unpause. **Never re-invoke** on confirm. If the operator wants a retry, they mint a **new** `stableActionId` (new trigger or explicit retry token), which is a new command id.

This is exactly `CommandExecution`’s Attempted/Unknown model, lifted to the processor snapshot so the graph stops rather than hammering a provider.

### Step 6 — Decision neuron (no tools)

New grain in the AI module, **not** a change to `AgentNeuron` / `BrainTools`.

- Input: signal body + output JSON schema from capability config.
- Call the existing model client **without** `AIFunction` tools (do not pass `BrainTools.For`).
- Validate output JSON against schema; on failure, do not `Announce`; leave unapplied or save a visible `DecisionInvalid` in snapshot and skip downstream.
- `Announce(Signal.Create("Decision", json))` only after validation.
- Tests assert the request had an empty tool list and that a tool-shaped model output is rejected.

Preserve `BrainTools` for the conversational Session agent.

### Step 7 — Wire describe/call and keep recipes

- Register `IBehavior` and processor interfaces in the module manifest so `NeuronInvoker.Describe` lists `save/validate/start/stop/resolve/read`.
- Assistant composition uses **call** on `behavior:{id}` (existing tool). No new BrainTools entries required for v2.
- Leave `RecipeNeuron` intact. New examples should save behaviors; old `Instruct` recipes still run.

### Step 8 — Tests and docs last

- File-backed restart tests in `src/Testing/` following `BrainSimulation` (`PersistenceDirectory`, `RestartSiloAsync`, `NeuronOptions.RetryReminderPeriod` already 2s).
- Update `docs/v2/PLAN.md` checklist only after evidence. Architecture/context docs after tests exist.

---

## 5. Risk cases (must be tests, not comments)

| # | Case | Expected |
|---|---|---|
| R1 | Start command Completes, silo killed before `Lifecycle` reaction | After restart, reminder/pending runs start; same owned ids; no duplicate leases |
| R2 | Crash after `SaveAsync(Desired=Starting)` and half the `ConnectOwned` calls | Reconcile resumes remaining leases; completed `ConnectOwned` command ids replay; no second edge |
| R3 | Crash inside `ConnectOwned` after Attempted, before terminal | Source neuron: retry of that command id → `CommandOutcomeUnknownException`. Behavior pauses that edge, does not mint a new lease id. Recovery of the **source** command journal + behavior reconcile eventually sees the terminal or re-reads synapses |
| R4 | Two behaviors share one source, different downstreams | Two leases on different `(to,type)`; stop A does not drop B |
| R5 | Two behaviors want the **same** `(from,to,type)` | Second Start/Validate fails collision; no disconnect of the first |
| R6 | Manual/recipe edge exists; behavior Start wants that edge | Validate/Start reject; Stop of behavior does not `Disconnect` it |
| R7 | Agent `disconnect` tool on a leased edge | Reject; edge remains |
| R8 | Save revision 2 while revision 1 Running | Latest stored; Active stays 1; Start(2) rejected until Stop(1) |
| R9 | Repeated Start/Stop same `CommandId` | Replay Completed; no extra Connect/Disconnect |
| R10 | Repeated Start **new** `CommandId`, same revision, already Running | Accept no-op (Desired already Running); no duplicate processors |
| R11 | Filter/map schema mismatch at Start | Reject before any `ConnectOwned` or `Configure` |
| R12 | Filter/map schema mismatch at runtime (payload) | Do not `Announce`; do not `SaveAsync` as applied if the policy is fail-closed — drop with a visible counter in processor snapshot, do not poison `AppliedBy` for a skipped invalid body if you still need to advance; **decide one**: recommended: `SaveAsync` skip-record so the signal is applied and not retried, downstream not announced |
| R13 | Action invoke unknown outcome | Processor paused; restart does **not** re-invoke; `Resolve(confirm)` completes without call; `Resolve(abort)` without call |
| R14 | Action target is a local idempotent command | Unknown still pauses if crash mid-attempt (kernel rule). That is correct: do not special-case local vs remote except `HasUncertainExternalEffect` for **Failed** (failed local → fail; failed external → pause) |
| R15 | Decision model returns extra properties / tool call | No downstream `Decision`; snapshot records rejection; reaction applied so it does not loop |
| R16 | Stopped processor receives a signal | Admit/journal as today, reaction no-ops (no Announce). Do not throw in a way that blocks the source forever; returning from `ReceiveAsync` without save still re-runs — so **must `SaveAsync` a stopped-skip** or the kernel’s applied-by on the **journal neuron** applies. Confirm whether filter is snapshot or journal: snapshot processors **must** `SaveAsync` to set `AppliedBy` or the reaction retries forever. Stopped skip = `SaveAsync` same config, no Announce |
| R17 | `Announce` drain crash (`IReactionCrashPoint.AfterSnapshotSave`) | Existing outbox test pattern: remaining `Announcements` replay; no duplicate if downstream is idempotent on `SignalId` |
| R18 | Behavior Save with invalid JSON schema | `CommandRejectedException`; no `Lifecycle` work; snapshot unchanged |
| R19 | Owned processor `Configure` then Start crash before upstream Connect | Processor configured, no input yet; restart Connects; no lost events if source has not fired (sources that fire during Starting: either buffer at source or accept at-least-once after Running — **do not open upstream until all processors configured**, as specified) |
| R20 | Historical recipe still `call`s a module | Unchanged; `$commandId` still random (known pre-existing); do not “fix” RecipeNeuron in this change unless a test already requires it |

R3 is the sharpest edge: **never mint a new `LeaseId` or `CommandId` after unknown**. Recovery is reconcile + replay, not a new attempt.

---

## 6. Files to add vs touch

**Add**

- `src/Kernel/DigitalBrain.Contracts/Behaviors/*.cs` — definition, commands, view, lifecycle enums
- `src/Kernel/DigitalBrain.Contracts/Synapses/IOwnedSynapses.cs`
- `src/Kernel/DigitalBrain/Descriptors/CapabilityRegistry.cs`
- `src/Kernel/DigitalBrain/Neuron/BehaviorNeuron.cs`
- `src/Kernel/DigitalBrain/Neuron/FilterNeuron.cs`
- `src/Kernel/DigitalBrain/Neuron/MapNeuron.cs`
- `src/Kernel/DigitalBrain/Neuron/ActionNeuron.cs`
- `src/Modules/AI/AI/DecisionNeuron.cs` (or Kernel if model client is abstracted)
- Tests under `src/Testing/` (behavior lifecycle, leases, restart, pause)

**Touch (minimal)**

- Synapse write path in the neuron base (lease checks; `IOwnedSynapses` implementation)
- `DigitalBrainRuntime` / module manifest — register grain types + capabilities + descriptors
- `docs/v2/PLAN.md` — checklist after evidence
- **Do not** touch `BrainTools.cs` unless a test proves describe/call cannot reach `IBehavior` (it should, via existing `call`)
- **Do not** change `CommandExecution` phases
- **Do not** change `Neuron<TState>` except if `Synapse` storage is there
- **Do not** change `RecipeNeuron`

If `Synapse` lives in `src/Kernel/DigitalBrain.Contracts/Synapses/`, additive optional fields **or** a sibling collection on grain state — prefer sibling collection to keep `db.v3` aliases untouched.

---

## 7. Implementation sequence vs PLAN.md checklist

Matches remaining checklist items, ordered for durability first:

1. Serializable contracts + schema validation (Step 1)
2. Node/connection ownership + compatibility tests (Step 2)
3. Behavior lifecycle on existing work/storage (Step 3–4)
4. Filter/map/action/decide processors (Steps 5–6)
5. Capability discovery via existing describe/call (Step 7)
6. Provider receipts / Elon-post examples — **after** R13 pause tests are green; use simulated provider only
7. Regression + docs

---

## 8. Verification commands

Run from the repo root, high severity, no CodeGraph mutation. Adjust the test project name if the sln uses a different MTP host (existing DigitalBrain test executable).

```powershell
dotnet build DigitalBrain.sln -p:CodeGraphRefresh=false
dotnet test src/Testing/DigitalBrain.Testing --severity high --no-build
```

If tests are a dedicated MTP exe (common in this repo):

```powershell
dotnet exec path\to\DigitalBrain.Tests.dll --severity high
```

Focused filters after they exist:

```powershell
dotnet test src/Testing/DigitalBrain.Testing --severity high --filter "FullyQualifiedName~Behavior"
dotnet test src/Testing/DigitalBrain.Testing --severity high --filter "FullyQualifiedName~OwnedSynapse"
dotnet test src/Testing/DigitalBrain.Testing --severity high --filter "FullyQualifiedName~ActionPause"
```

Restart/persistence tests **must** set `BrainSimulationOptions.PersistenceDirectory` (temp dir) and call `RestartSiloAsync`. In-memory cluster tests do not prove recovery.

Opt-in real Twitter/provider tests: run only if credentials are present; report skipped otherwise. Simulated receipt path is mandatory.

Aspire: after the library tests pass, `aspire do build` / existing AppHost test host — do not require a live dashboard for kernel lifecycle tests.

Finish criteria: R1–R20 have automated evidence; existing recipe/invoker/agent tool tests still pass; no new storage provider; no change to `SnapshotEnvelope` field ids.

---

## 9. What not to do

- Do not persist executable code or generate processor classes from the definition.
- Do not give the decision neuron `BrainTools` or any `INeuronInvoker` access.
- Do not implement start/stop effects inside `CommandExecution`’s `execute` delegate (`SaveAsync`/`Announce`/`Connect` will throw or violate Attempted/unknown).
- Do not use `Guid.NewGuid()` for action command ids or lease ids.
- Do not `Disconnect` by `(from,to,type)` without a lease on stop.
- Do not convert `Instruct` recipes on read.
- Do not add `CommandPhase.Paused`. Pause lives in **processor/behavior snapshot**; the command log already has Attempted/Unknown.
