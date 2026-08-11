# DigitalBrain

A personal "alive OS": an Orleans-based brain of **neurons** (durable grains) exchanging
**synapses** (facts) whose runtime topology — the **synapse graph** of **connections** —
is data the owner and the assistant rewrite while it runs. Architecture record and open
decisions: [UNIFIED-ARCHITECTURE.md](UNIFIED-ARCHITECTURE.md) (current) and
[INTERCONNECT-REVIEW.md](INTERCONNECT-REVIEW.md) (evidence base).

> **Owner amendment — 2026-08-11:** production source is the current behavioral truth. The central
> automated-test project was intentionally deleted; do not create or run .NET or Flutter tests
> during this refit. Final hardening will design module-owned test projects/frameworks. Keep
> Salesforce Contracts as the product boundary for neuron and synapse interfaces.

## Commands

```bash
dotnet build DigitalBrain.slnx -warnaserror --nologo                  # full solution, 0 warnings expected
pwsh scripts/gate.ps1                                                 # .NET source-build gate
pwsh scripts/gate.ps1 -Flutter                                        # plus production Flutter lib analysis
dotnet run --project src/Kernel/DigitalBrain.AppHost                  # full stack (Docker: Azurite, Qdrant, Ollama)
```

Flutter (`src/Modules/UI/Flutter/{core,kit,shell}`): `flutter analyze lib` per package. Do not run
automated tests. Owner scripts are .NET single-file apps in `src/Kernel/DigitalBrain.Scripting/*.cs`
(`#:project` directives, run with `dotnet run <file>.cs -- --ConnectionStrings:clustering "…"`).

Stop AppHost before every build; running silos hold output files open.

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
- **Identity/workspace boundary** (Kernel host): ASP.NET Core Identity cookie auth backed by
  Azure Tables; the fallback policy requires authentication on product endpoints. The
  bootstrap/login/logout/me lifecycle and OAuth callback are intentionally anonymous.
  Development-only loopback bypass is explicit. The host derives `ActorContext` and
  principal-scoped chat/surface names; client identity never crosses into durable commands
  unverified.
- **Durable conversation turns**: Chat queues one active turn per conversation and starts an
  `IExecution`; additional turns remain FIFO. HTTP/SSE is an observer—disconnecting never cancels
  work. Explicit versioned cancel advances the queue only after the Execution terminal bridge.
  Execution owns attempts, blockers, liveness, bounded receipts/operations, and reconciliation of
  `OutcomeUncertain`; the chat worker owns the AI stream and reports only Attempt facts.
- **MCP gateway** (`IMcp`, grain `mcp`, instance = server key, e.g. `mcp:dev/salesforce`):
  external SaaS is NEVER per-action contracts — the server's live catalog IS the surface.
  `db.mcp.list-tools` answers it; `db.mcp.call-tool` invokes through the OAuth rail
  (all catalog tools are callable; provider OAuth + verified per-principal integration is the
  authority boundary, and destructive metadata remains visible for audit).
  `FireRowsAs` fires each tabular result row as a named synapse (shape rows in the query:
  SOQL column aliases → `ui.chart-point` fields) so results flow through the graph.
  Salesforce and Gmail are `McpServerDefinition`s on the same bounded PKCE rail; tokens are
  protected and keyed by verified principal. The OAuth callback is `/oauth/callback`; codes are
  host-only and one-shot. The unkeyed `IChatClient` IS Gemma4 (the main model); `ask_llama`
  exists only when the owner names llama.
- **Self-programming**: the Assistant (Gemma4/Ollama) carries exactly THREE constant tools
  (`SystemTools`): `find_capabilities(intent)` — hybrid search over the in-process
  `CapabilityIndex` (keyword floor, embeddings enrich when a generator exists, nothing can
  stall); `get_neurons(type?)` — live instances + connections; `fire(contract, arguments,
  target?)` — bind, validate, send via session, return the reply. Graph verbs are ordinary
  contracts fired like everything else. Canonical choreography: wire the graph
  (fire db.connect with a morph), THEN trigger the source — data never transits the model.
  fire errors are correctable text (near-matches, real signatures, live instances on
  guessed identities); unavailability refuses settled with the fix path in the message.
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
6. **Manifests are reflected, never written** (`ModuleReflection.ManifestOf(contractsAssembly)`).
   A neuron's ContractId = its interface `[Alias]` (else trimmed name, lowercased); Accepted
   from `IHandle<T>`, Emitted from `RequestSynapse<TReply>` replies, module vocabulary =
   every synapse type in the assembly (`Facts`). Contracts carry no `[Description]` — names
   ARE the documentation; `IEmit<>` is gone (nothing ever consumed it).
7. **Models pass names where schemas want GUIDs.** `BindModelArguments` derives a
   deterministic GUID from any non-GUID string on a Guid property (stable name = stable
   identity → replace/disconnect by name). Missing value-type JSON fields bind silently
   to defaults — validate in handlers.
8. **Declaring `IHandle<T>` on any class puts T in the broadcast catalog** (reflection
   manifest) and spawns per-correlation ghost receivers on every Emit of T. Routed-only sinks
   must not accidentally become broadcast handlers.
9. Keyword god-switches in handlers are banned — two were deleted after one silently
   swallowed any chat message containing "chart".

## Conventions

- **Modules expose contracts and implementations separately.** Contracts assemblies own neuron
  interfaces and synapses; implementation assemblies own grains and optional `Core.IModule` DI
  hooks (AI, Google, Memory, Salesforce, Execution, UI). Composition
  (`DigitalBrainRuntime.Add(silo, ModuleAssemblies)`) reflects manifests and scans implementations;
  there are no handwritten compiled manifests or `DigitalBrain:Modules` class-name gate.
- **Salesforce Contracts stays.** It is the permanent module boundary for Salesforce neuron and
  synapse interfaces even while the generic MCP rail supplies today's runtime surface.
- **SOURCE → GREEN → GRILL → GATE → COMMIT**: inspect production implementations and routes,
  make the smallest coherent change, adversarially review the diff against the kernel traps,
  run the build/static gate, then commit on the Stage-1 branch. Automated testing is deferred
  to a per-module framework in final hardening.
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

- W2 is complete: `WantsTimeButton`/`ShowTime` and its transform are gone; `Responded.Author`
  is populated. Optional Message/Reply naming and W3 manifest-drift guard remain separate work.
  W4 offer expiry and W5(b) read-only broadcast topology landed; full catalog/graph unification
  still needs its own review.
- Behavior Studio (Flutter shell + core BehaviorClient) renders demo fixtures against a
  host that does not exist — kept on purpose. Direction: build the behavior host on the
  scripting capability, with the assistant authoring single-file C# scripts that compile
  into installable behaviors. Needs a dedicated design session before any code.
- Per-emission graph-call cache — measure under aspire before optimizing.
- **Refusal visibility (top priority)**: settled refusals produce no reply, so a request
  loop (fire tool) sees only a 15s timeout — the refusal REASON (e.g. connect-time morph
  validation) never reaches the model. Decide: kernel refusal-replies for RequestSynapses
  vs per-contract error-bearing responses (the Salesforce pattern). Live round 7 proved
  the model reports such failures honestly but cannot self-correct without the reason.
- Surface-events → windowing bridge (shell does not consume `SceneOpened` yet); unlocks
  diagram/graph windows and "show me my graph" via `OpenSurface`.
- Automated testing is intentionally deferred. Final hardening must create module-owned test
  projects/frameworks; do not restore one central suite.
- `/brain/topology` + `/graph/events` exist; delivery-pulse event stream for edge
  animation does not yet.
- Stage 2 starts with Conversation extraction (`UI → Conversations ← AI`), then formalizes SDK
  OAuth/authorization and webhook-ingress rails. Project consolidation, the graph-neuron rename,
  and the duplicated AppHost/Kernel module catalog remain explicit later decisions.
