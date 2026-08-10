# DigitalBrain

A personal "alive OS": an Orleans-based brain of **neurons** (durable grains) exchanging
**synapses** (facts) whose runtime topology — the **synapse graph** of **connections** —
is data the owner and the assistant rewrite while it runs. Architecture record and open
decisions: [UNIFIED-ARCHITECTURE.md](UNIFIED-ARCHITECTURE.md) (current) and
[INTERCONNECT-REVIEW.md](INTERCONNECT-REVIEW.md) (evidence base).

## Commands

```bash
dotnet build DigitalBrain.slnx                                        # full solution, 0 warnings expected
dotnet test src/Tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj    # .NET suite (in-memory 2-silo cluster)
dotnet run --project src/Kernel/DigitalBrain.AppHost                  # full stack (Docker: Azurite, Qdrant, Ollama)
```

Flutter (`src/Modules/UI/Flutter/{core,kit,shell}`): `flutter test` and `flutter analyze`
per package. Owner scripts are .NET single-file apps in `src/Kernel/DigitalBrain.Scripting/*.cs`
(`#:project` directives, run with `dotnet run <file>.cs -- --ConnectionStrings:clustering "…"`).

Run the .NET suite **without** the aspire stack up — the timing-window tests flake on a
saturated machine.

## Architecture spine

- **Neuron** = durable grain (`Neuron` base, Core). Per-neuron incoming/outgoing **journals**
  are the audit source; **journal-is-outbox**: a turn's fact + outbox entries commit in ONE
  `WriteStateAsync`. Delivery is at-least-once + receiver dedupe by `SynapseId` = effectively once.
- **Turns are single-threaded.** `NeuronConcurrency` refuses `[Reentrant]`/`[AlwaysInterleave]`/
  `[ReadOnly]`/`[StatelessWorker]` (kernel `ReadJournal` excepted). No handler may call back
  into its emitter.
- **Emit vs Send**: `EmitAsync` = fact, receivers decided by data (broadcast catalog ghosts +
  synapse-graph connections). `SendAsync(receiver, …)` = directed, never consults the graph.
  Those are the kernel-side `Neuron` verbs; the client (`IDigitalBrain`, `NeuronReference`)
  speaks one verb: `FireAsync(synapse)` = emit semantics, `FireAsync(receiver, synapse)` /
  `Get<T>(name).FireAsync(...)` = directed (F1, landed).
- **Synapse graph** (`ISynapseGraph`, grain `synapsegraph:owner/graph`): durable `Connect`/
  `Disconnect` requests answered with `Connected`/`Disconnected`; one record `SynapseConnection`
  (ConnectionId, Source, SynapseAlias, Target, Transform?, ExpiresAt?). Transformed connections
  route via `ConnectionRelayNeuron` (grain `relay:owner/{connectionId}`).
- **Transforms**: DI-registered `ISynapseTransform` by name, or declarative data
  `to:<alias>{Target=Source}` (JSON morph, parsed by the relay — no code needed).
- **Vocabulary neurons** (UI module): `chart` (`ui.chart-point`), `diagram` (`ui.node`/`ui.edge`,
  upsert-by-identity), `button` (clicks → `ui.button-activated`, offers arm connections),
  `chat` (responder resolved from graph role `role:responder`, fallback assistant; also
  handles `ui.note` → transcript line and `ui.timer-card` → clock offer in the turn).
- **Timer** (Time module): `timer` neuron; `time.start-timer`/`time.cancel-timer` are model
  tools; scheduling and elapse EMIT `time.timer-scheduled`/`time.timer-elapsed` — the graph
  routes them (assistant recipe: morph to `ui.timer-card`/`ui.note` into chat). Kit renders
  `KitTimerPart` with `KitClock` (countdown face; wall clock in windowing).
- **Self-programming**: `db.connect`/`db.disconnect` + introspection requests are capabilities;
  the Assistant (Gemma4/Ollama) always carries them as tools and its instructions explain the
  graph. Proven live: a chat request wired `chat.responded → chart` end to end.
- **Orleans Streams are deliberately unused** for the interconnect (provisioned only).
  Do not move delivery onto them — see the review's fit matrix.

## Kernel traps (each cost a debugging session — do not rediscover)

1. **A turn cannot await the effect of its own Send.** The outbox drain timer only fires
   between turns; awaiting a delivery mid-turn starves until timeout. Use
   `Neuron.FlushOutboxAsync(ct)` to drain inline (see chat's arm-before-offer).
2. **Zero-receiver emissions create NO outbox entry.** Journaled, never delivered, never
   retried. An emission racing ahead of its connection is silently lost — confirm routes
   before making a clickable/observable thing visible.
3. **Grain-call reification.** `OutgoingReificationFilter` turns every non-framework grain
   call between neurons into durable `CapabilityRequested`/`Completed` journal facts.
   Kernel infrastructure interfaces must be listed in `CapabilityInvocation.FrameworkInterfaces`
   (`INeuron`, `ISessionNeuron`, `ISynapseGraph`).
4. **Settled vs retried failures.** Handler exceptions are retried every 50ms up to 1000
   attempts/30min unless the exception type is `[SettledDeliveryFailure]`. Deterministic
   validation/refusal must throw `NeuronAuthorizationException` (settles as "refused",
   commits the inbound cause).
5. **Only `RequestSynapse<TResponse>` synapses materialize as model tools**
   (`SynapseCapabilityTool.Materialize`). Plain synapses are silently skipped.
6. **Capability manifest neuron `ContractId` = the interface's `[Alias]`**
   (e.g. `db.synapse-graph`), never the grain type string. Hand-written `*.Compiled.cs`
   manifests drift — keep `GraphCapabilityManifestProofs`-style guards; a reflection
   cross-check test is planned (W3).
7. **Models pass names where schemas want GUIDs.** `BindModelArguments` derives a
   deterministic GUID from any non-GUID string on a Guid property (stable name = stable
   identity → replace/disconnect by name). Missing value-type JSON fields bind silently
   to defaults — validate in handlers.
8. **Declaring `IHandle<T>` on any class puts T in the broadcast catalog** (reflection
   manifest) and spawns per-correlation ghost receivers on every Emit of T. In tests,
   use `OnUnboundSynapseAsync` overrides for routed-only sinks.
9. Keyword god-switches in handlers are banned — two were deleted after one silently
   swallowed any chat message containing "chart".

## Conventions

- **TDD is mandatory**: failing test first (or mutation-verify if code ran ahead), minimal
  green, then refactor. Two test kinds only: NeuronTest-style (one neuron's contract) and
  DigitalBrainTest-style (cross-neuron through the cluster) — one project,
  `src/Tests/DigitalBrain.Tests` (fixture: `InProcessTestClusterBuilder` + shared
  `VolatileJournalStorageProvider` + `UseInMemoryReminderService`).
- No `/// <summary>` boilerplate. Comments only for constraints the code cannot express.
  Naming carries the meaning; kernel vocabulary: Neuron, Synapse, journal, outbox, owner,
  Emit/Fire/Send/Deliver, Connect/Connected/Disconnect/Disconnected/connection.
- Grain-turn awaits use `ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)`.
- `TreatWarningsAsErrors` + preview analyzers everywhere; net11.0 preview SDK; C# 14
  features are in use (`extension(Type)` static members — see `DigitalBrainScriptHost`).
- Flutter kit models use plain names (`GraphNode`, `GraphEdge`); kit *widgets* use the
  `Kit` prefix (`KitGraph`, `KitChart`). The kit is standalone — never import core/shell
  into it.
- Wire aliases are permanent once real data exists: `db.*` kernel, `ui.*` vocabulary,
  `chat.*`/`probe.*` domains.

## Open work (owner-gated)

- W2 cleanup: delete the last keyword demo (`WantsTimeButton`/`ShowTime`), add `Author`
  to `Responded`, optional `Message`/`Reply` rename. W3: manifest-drift guard.
  (W4 lifecycle landed 2026-08-10: offer expiry 24h, mutation-time sweep, `Guid.Empty`
  refusal; button-grain state retention deliberately deferred until storage pressure is
  measured. W5(b) landed: broadcast tier shown read-only in topology; full catalog/graph
  unification (c) still needs its own review.)
- Behavior Studio (Flutter shell + core BehaviorClient) renders demo fixtures against a
  host that does not exist — kept on purpose. Direction: build the behavior host on the
  scripting capability, with the assistant authoring single-file C# scripts that compile
  into installable behaviors. Needs a dedicated design session before any code.
- Per-emission graph-call cache — measure under aspire before optimizing.
- Surface-events → windowing bridge (shell does not consume `SceneOpened` yet); unlocks
  diagram/graph windows and "show me my graph" via `OpenSurface`.
- Flutter core has pre-existing `activateControl` test drift (spawned task).
- `/brain/topology` + `/graph/events` exist; delivery-pulse event stream for edge
  animation does not yet.
