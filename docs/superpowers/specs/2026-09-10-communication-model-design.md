# DigitalBrain communication model

Status: converged 2026-09-10 after seven adversarial rounds between Claude and Codex (round
transcripts kept outside the repo). Supersedes the applications/scripting subsystem and the
`IHandle<T>` neuron layer in this repo. Ports the model proven in `digitalbraincore` and
adds typed commands on top of it.

## The model in one paragraph

A neuron is a durable Orleans grain with one receive slot and two channels. A **command** is
a typed method on the neuron's own grain interface: addressed by the caller, executed
synchronously and locally inside the target's turn, audited in a bounded command journal,
deduplicated by an explicit `CommandId`, and only ever allowed to stage work and mutate
journaled state. A **signal** is a type name plus a JSON body that travels along synapses:
addressed by the graph, accepted into a bounded durable pending queue, and reacted to later
in the neuron's own turns, at least once or until cancelled. Reactions are where a neuron
talks to the world: they fire signals, call other neurons, run models and save snapshots.
Every neuron exposes the same ten kernel operations; an AI agent reads a neuron's
interfaces through a descriptor table and calls typed methods through the same table that
generates its tools. There is one brain per silo and no identity inside the neuron layer.

## 1. Kernel interface

```csharp
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Fire)), ResponseTimeout("00:05:00")]
    Task<FireOutcome> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken ct = default);
    [Alias(nameof(Connect))]    Task Connect(NeuronId target, string signalType);
    [Alias(nameof(Disconnect))] Task Disconnect(NeuronId target, string signalType);
    [Alias(nameof(Deliver)), AlwaysInterleave, ResponseTimeout("00:05:00")]
    Task<DeliveryAdmission> Deliver(SignalDelivery delivery, CancellationToken ct = default);
    [Alias(nameof(CancelReaction)), AlwaysInterleave]
    Task CancelReaction(SignalId pending);
    [ReadOnly, Alias(nameof(ReadState))]    Task<IReadOnlyList<SignalDelivery>> ReadState();
    [ReadOnly, Alias(nameof(ReadSynapses))] Task<IReadOnlyList<Synapse>> ReadSynapses();
    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadJournal)), ResponseTimeout("00:05:00")]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadCommands))]
    Task<CommandJournalRead> ReadCommands(long afterSequence);
    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadPendingCount))]
    Task<int> ReadPendingCount();
}
public enum DeliveryAdmission { Accepted, Duplicate, Busy }
```

Plus the internal one-way `INeuronInbox.Drain`, the only one-way call in the system.
`Deliver` interleaves because it only accepts: all of its staging happens synchronously
before its single persist await, and without interleaving two neurons whose reactions
fire at each other deadlock (A's `Fire` awaits B's `Deliver` while B's reaction awaits A's
`Deliver`). The kernel uses of `[AlwaysInterleave]` are therefore `Deliver`, `CancelReaction`,
`ReadJournal`, `ReadCommands` and `ReadPendingCount` (the size of the pending queue, so that
`Busy` is observable while a reaction runs).
`ReadJournal` and `ReadCommands` interleave and serve the committed view only. `ReadState`
and `ReadSynapses` are serialized turns.

A module neuron extends `INeuron` with its own `[Alias]`ed interface of typed commands and
`[ReadOnly]` queries:

```csharp
[Alias("ui.chat")]
public interface IChat : INeuron
{
    [Alias("send")] Task<Accepted<TurnId>> Send(SendMessage message, CancellationToken ct = default);
    [ReadOnly, Alias("transcript")] Task<ChatTranscript> ReadTranscript(ReadTranscript query);
}
public sealed record SendMessage(CommandId Id, string Text, IReadOnlyList<ContextRef> Context) : Command(Id);
```

`[ReadOnly]` is allowed on module methods. `[AlwaysInterleave]` is kernel-only; the
concurrency validator allows exactly the four kernel uses above.

## 2. Base class

```csharp
public abstract class Neuron : DurableGrain, INeuron, INeuronInbox
{
    protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken reaction);
    protected Task<FireOutcome> FireAsync(Signal signal, NeuronId? to = null, CorrelationId? correlation = null, CancellationToken ct = default);
    protected SignalId Schedule(Signal signal, CorrelationId? correlation = null);
    protected Task<TResult> ExecuteCommandAsync<TArgs, TResult>(
        CommandDescriptor command, TArgs args, JsonTypeInfo<TArgs> argsJson, JsonTypeInfo<TResult> resultJson,
        Func<TArgs, TResult> execute) where TArgs : Command;
    protected Task PersistAsync();
    protected ReactionContext? ReactionContext { get; }
    protected TimeProvider TimeProvider { get; }
}
public abstract class Neuron<TState> : Neuron where TState : class
{
    protected TState? State { get; }
    protected Task SaveAsync(TState value, CancellationToken ct = default);   // reactions only
}
public abstract record Command([property: Id(0)] CommandId Id, [property: Id(1)] long? ExpectedVersion = null);
public sealed record Accepted<TReceipt>(TReceipt Receipt, SignalId Work);
```

Durable members, all in the one op log: `Incoming`, `Outgoing` and `Commands` bounded
windows (512 entries / 512 KiB) each with a `CommittedSequence`; `Pending:
IDurableQueue<SignalDelivery>` capped at 256; `PendingCancelled: IDurableSet<SignalId>`;
`ReactedIds` ring of 256; `Latest` per type (256 types); `Synapses`; `Dedup:
IDurableDictionary<CommandId, CommandOutcome>` holding 1024 resolved outcomes, unresolved
never evicted, admission refused when full of unresolved. `Neuron<TState>` owns the only
snapshot facet.

## 3. Signals: admission, reaction, cancellation

- `Deliver`: capture caller metadata; `Duplicate` if the signal id is pending or in
  `ReactedIds`; `Busy` if `Pending` is full, writing nothing; else append incoming journal,
  set latest, enqueue, persist, wake, `Accepted`. `FireAsync` awaits every target's
  `Deliver` and reports non-acceptance per target.
- `Schedule(signal)` is the local form used inside commands: same checks, same enqueue, no
  flush; capacity exceeded throws `NeuronBusyException` before anything is staged. It is the
  one permitted self-delivery and creates no synapse and no outgoing entry.
- `Drain` (one entry per call): peek head; if in `PendingCancelled` → dequeue, remove, push the
  id to `ReactedIds` (a re-delivery of a cancelled id is `Duplicate`), persist;
  else set `ReactionContext`, clear and rebuild `RequestContext` from the persisted delivery,
  call `ReceiveAsync` with a token linked to the activation and a per-entry source, then
  dequeue, push id to `ReactedIds`, persist. Throw → log, arm the retry timer (1 s → 60 s
  exponential, `Interleave = false`, `KeepAlive = true`), leave the head. Activation re-arms if
  a head exists. A grain timer needs an activation, so while the head has failed the neuron
  also holds one Orleans reminder (`retry`, minimum period) that reactivates it after a cold
  restart; the reminder is unregistered when the head clears.
- `CancelReaction(id)`: no-op if `id` is neither pending nor reacting; else add to
  `PendingCancelled`, persist, and cancel the reacting entry's token if it is the one running.
  Cancellation is observed at the reaction's next cooperative observation of its token.
- Guarantee: a signal accepted by `Deliver` is reacted to at least once, or cancelled,
  across restarts, with retries needing no new traffic. Loss means `Busy`, which the emitter
  sees. The incoming journal is history, not schedule.
- Reactions may await reads, signal admission and I/O, but never wait for a downstream
  reaction to complete.

## 4. Commands

Wrapper timeline; the only awaits are the two persists:

1. Serialize args at the target; > 64 KiB, capacity, or id reuse with different args →
   stage `Rejected` with bounded metadata, persist, throw. Consult `Dedup`: resolved with
   matching caller, interface, method and SHA-256 of canonical args → return the stored
   result or rethrow the stored failure; `Unknown` → fall through as a new incarnation;
   `Attempted` → throw `CommandOutcomeUnknownException`.
2. Stage `Attempted` (with `ArgsJson`, `Incarnation`), `Dedup[id] = Attempted`, **await
   `PersistAsync()`**.
3. `execute(args)` synchronously. It may `Schedule` and mutate journaled members. It may not
   save a snapshot, fire, or call any grain: `SaveAsync` and `FireAsync` throw when
   `ReactionContext` is a command, and the kernel's outgoing call filter rejects calls whose
   source activation is executing a command. Stage `Completed` (result ≤ 64 KiB, else omitted
   with a note) or `Failed` (error ≤ 4 KiB) in the same synchronous run.
4. **await `PersistAsync()`**, return.

Because the turn never yields between step 2 and step 4, no interleaving request can flush
effects without their outcome. Execution failure does not roll back staged effects; they
persist together with `Failed`. Recovery discards whatever storage did not commit.

`Unknown` means "attempted, no committed effect", so a retry with the same id re-executes as
the next incarnation. Generic deduplication is guaranteed for the last 1024 resolved commands
per neuron and nothing beyond; modules needing more carry domain receipts with the same stated
bound (`ChatState.TurnByCommand`, 1024) or `ExpectedVersion` preconditions.

Acceptance commands return `Accepted<TReceipt>` whose `Work` is the scheduled signal id.
For chat, `TurnId` *is* that id: fresh per incarnation, used by the reaction that creates the
turn. Modules expose no `Cancel` methods; edges call `CancelReaction(work)` directly.

Audit records are append-only transitions:

```csharp
public sealed record CommandRecord(long Sequence, CommandId Id, int Incarnation, string Interface, string Method,
    CommandPhase Phase, NeuronId Caller, CorrelationId Correlation, SignalId? Causation,
    string? ArgsJson, string? ResultJson, string? Error, DateTimeOffset At);
public enum CommandPhase { Rejected, Attempted, Completed, Failed, Unknown }
public sealed record CommandJournalRead(long ResumeSequence, long EarliestRetained, bool Gap, IReadOnlyList<CommandRecord> Delta);
```

`JournalRead` gains the same `EarliestRetained` and `Gap`; cursor, gap and delta come from
one committed view.

## 5. Persistence, failure and recovery

```csharp
protected async Task PersistAsync()
{
    if (_faulted) throw new NeuronRecoveringException(Id);
    try { await WriteStateAsync(_activation.Token); }                    // never a request token
    catch (OperationCanceledException) when (_activation.Token.IsCancellationRequested) { throw; }
    catch (Exception e) { _faulted = true; await RecoverAsync(e); }
}
```

`RecoverAsync`: `StateManager.RevertPendingChangesAsync(_activation.Token)` reloads journaled
state in place; then, still fenced, command reconciliation resolves `Dedup` entries from
persisted terminal records and converts abandoned `Attempted` incarnations to `Unknown`,
persisting through the recovery path; only then `_faulted` clears and the original mutating
call fails with `NeuronPersistenceException`. Reconciliation or revert failure leaves the
activation fenced, calls `DeactivateOnIdle()`, and every call including interleaving reads
throws `NeuronRecoveringException` from method entry until reactivation replays from storage.
The same reconciliation runs on every activation.

A mutating turn holds the activation until the write settles, so no serialized read can
observe an unflushed mutation. Snapshot saves are reaction-only; a failed `SaveAsync` is a
failed reaction, retried from the head after the facet re-reads storage.

The journal storage provider is wrapped with a 120-second cancellation budget per complete
append, replace or recovery read, including retries and stream consumption; it awaits
settlement of the underlying operation before returning and never abandons an active
storage call. Azure client retry settings are not an aggregate deadline. Orleans'
`MaxRequestProcessingTime` stays at default and is a detector, not a deadline.

## 6. Attribution and causation

- One kernel `IOutgoingGrainCallFilter` sets `RequestContext["db.caller"]` to
  `context.SourceId` when the call originates in an activation, else keeps the edge's value,
  in a fresh scope per call restored in `finally`. `Deliver` and the command wrapper capture
  caller, correlation and causation before any outbound call.
- `ReactionContext` is set by `Drain` (delivery) and the wrapper (command) and restored in
  `finally`; `FireAsync` reads causation from it. A signal fired by the reaction that a
  command scheduled has causation equal to the scheduled work's id, whose record carries the
  command id.
- Timeout at the caller means unknown; the target's audit is the truth; retrying with the
  same id reads the stored outcome.

## 7. Discovery and invocation

A descriptor table is built once at silo start per grain class: `GrainType → interface
aliases`, `interface alias → MethodDescriptor[]`.

| Element | Rule |
|---|---|
| Interfaces | any `[Alias]`ed interface extending `INeuron`; alias unique silo-wide |
| Methods | `[Alias]` required, unique per interface; `Task` or `Task<T>` only |
| Args | one DTO deriving from `Command` for mutators; zero or one DTO for `[ReadOnly]`; optional trailing `CancellationToken` |
| DTO fields | STJ source-generated context per Contracts assembly is the JSON contract; Orleans `[Id]`s are the wire contract |
| Types | records, primitives, string enums, `NeuronId`, ids, `DateTimeOffset`, `IReadOnlyList<>` and nullable of these |
| JSON | `Task` → `null`; omitted field → default, `required` enforced; `null` only for nullable members; nullability enforced at invocation |
| Summary | `<summary>` from the Contracts XML doc file when present |

`describe(neuron | interface)` returns descriptors. `call(neuron, interface, method, args)`
validates that the neuron's grain type implements the interface, else fails naming the
interfaces it does implement, then invokes a cached delegate over `GetGrain<TInterface>`.
Violations of the table fail at silo start naming interface and method.

Edges: MCP `fire, connect, disconnect, read, describe, call, cancel`; HTTP adapters over the
same; in-silo agents get the seven as `AIFunction`s plus typed functions generated from the
descriptors of the interfaces they are instructed to use.

## 8. Signals vocabulary and payloads

`Signal(Type, Body)`: type matches `^[A-Za-z]{1,64}$`, body is JSON ≤ 64 KiB. Modules ship a
vocabulary class of type names and body records with `Signal.FromJson<T>(string type, T body,
JsonTypeInfo<T>)` and `delivery.Body<T>(JsonTypeInfo<T>)`. Unknown vocabulary is journaled
and ignored without loading anyone's Contracts assembly. Routing follows synapses only; a
method alias is never a synapse type. Latest-per-type holds signals only; commands never touch
it.

## 9. UI edge

HTTP endpoints are thin adapters: commands map to typed calls, reads to `[ReadOnly]` methods,
streams to long-poll over `ReadJournal(Incoming, after)` on the person's session neuron
`NeuronId.Plain(login)`, woken by a broadcast notification published after each `Deliver`
persist. A cursor older than the retained window yields a `reset` event carrying a typed
projection from the neuron's own read method. Transcript and turn state live in the chat
neuron's snapshot, not in any bounded inbox.

## 10. Module migration rules

- `IModule.Configure(ISiloBuilder)`; three projects per module (Contracts, Module,
  Aspire.Hosting); neuron classes discovered by Orleans from the referenced assembly; the
  manifest lists modules only.
- Entities become `Neuron<TState>` with typed reads and commands.
- Execution module: its context model is ported into `ChatState.TurnContext` (path, digest
  with the existing recipe, schema hash, payload ≤ 64 KiB or blob reference, precedence,
  related turns; last 32 inline, older as references), assembled by the chat's reaction to
  `TurnRequested`; blob round-trips verified; then the module is deleted.
- Deleted outright: applications/scripting subsystem, `IHandle<T>`, `Entity<TState>`,
  `IDigitalBrain`, `DigitalBrainClient`, `IComposer`, `SignalSender`, weighted/learned
  synapses, `Watch`, owner/principal identity, Application Studio and its Flutter screen.

## 11. Invariants

Core 1–7 unchanged. Added:

8. A command appears in `Commands` either as a single `Rejected`, or as `Attempted` followed
   by exactly one of `Completed | Failed | Unknown`, per incarnation.
9. A `[ReadOnly]` method leaves no journal entry.
10. `describe` equals what `call` accepts; a wrong interface for a neuron type fails naming the valid ones.
11. A signal fired by the reaction a command scheduled has causation = the work id, whose record carries the command id.
12. `Drain`, `Connect`, `Disconnect`, `Deliver`, `CancelReaction` never appear in `Commands`.
13. Retry with a resolved id returns the stored outcome without executing; with `Unknown` it re-executes once as the next incarnation; with `Attempted` it throws `CommandOutcomeUnknownException`.
14. A `Schedule` that would exceed capacity fails the command before any state is staged.
15. An accepted signal is reacted to at least once or cancelled, across restart, with retries needing no new traffic.
16. `CancelReaction` during a long reaction is observed at the reaction's next cooperative token check; ids not pending or reacting are ignored.
17. After a persistence failure, no read on the activation returns state that storage does not hold; reconciliation runs before the fence lifts.

## 12. Implementation order

1. Port the core signal kernel (vocabulary, identity, routing, aliases, timeouts). Proof: `membrane`, `connect`, `fire` features.
2. Durable admission, pending queue, drain, retry timer, direct cancellation, recovery fence, reaction-only snapshot guard. Proof: invariants 14–17, `react`, `state`, new `recovery` feature.
3. Command descriptors, wrapper, bounded outcomes, dedup, reconciliation. Proof: 8, 12–14.
4. Scoped caller and reaction context, outbound stamping. Proof: 11, lineage scenarios.
5. Committed reads, retention gaps, typed reset projections, edge long-poll. Proof: 9, `journal`, `read`.
6. `describe`/`call`/`cancel` through MCP and HTTP. Proof: 10, `mcp`.
7. Migrate modules; port Execution context into chat; delete superseded code. Proof: 8–17, `chat`, `agent`.
