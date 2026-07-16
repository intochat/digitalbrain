# Everything Is a Neuron

**Status:** approved implementation design  
**Date:** 2026-07-16  
**Axiom:** Everything addressable in DigitalBrain is a Neuron. Differences are technical implementation traits, never a competing product taxonomy or runtime.

## 1. Outcome

DigitalBrain becomes one graph of scoped Neurons. Flutter, DigitalBrain MCP, Features, connectors, model workflows, sessions, approvals, activities, and UI destinations address the same Neuron identities. A Neuron may be deterministic, persistent, connector-backed, model-backed, projected to UI, or composed from several traits. Those traits change execution, not ontology. The implementation must remove specialized lifecycle, transport, catalog, projection, and approval machinery wherever the universal Neuron kernel already supplies the same behavior.

The first proof is:

```text
DigitalBrain MCP neuron invocation
    -> same Owner/Actor-scoped Chat Neuron
    -> durable state revision
    -> UI Neuron projection
    -> SurfaceFeed Neuron revision
    -> Flutter observes the change
```

## 2. Non-negotiable decisions

1. `INeuron` is a real universal runtime contract, not a documentation metaphor.
2. Every addressable product/runtime object has a stable `NeuronAddress`.
3. Orleans hosts one universal Neuron grain identity and lifecycle.
4. Typed domain interfaces such as `IGmail : INeuron` remain compile-time contracts.
5. Typed interfaces are implemented as generated or hand-written facets over the universal grain.
6. Flutter destinations and surfaces are UI Neurons.
7. MCP and Flutter never receive transport-specific copies of product state.
8. Transport audience and session ID are caller context, not product Neuron identity.
9. Synapses are typed, policy-relevant Neuron-to-Neuron relationships.
10. Grants and approvals use the same Synapse and journal authority.
11. External mutation always produces an Effect Neuron.
12. A connector can apply an external mutation only with proof from an approved Effect Neuron.
13. LLM execution is a Neuron trait, not an Agent runtime.
14. Feature execution is a Neuron module trait, not a second product runtime.
15. Orleans streams remain progress, fan-out, and observation infrastructure, not the command bus.
16. RFW is deleted.
17. External tenant MCP URLs are not product connections.
18. The public DigitalBrain MCP surface converges on `neuron_describe`, `neuron_read`, and `neuron_invoke`.
19. Existing tools may remain temporary aliases only while migration tests require them.
20. Code deletion is a primary acceptance criterion.

## 3. Universal identity

```csharp
[GenerateSerializer, Alias("digitalbrain.neuron-address.v1")]
public readonly record struct NeuronAddress(
    [property: Id(0)] BrainOwnerId OwnerId,
    [property: Id(1)] string SpaceId,
    [property: Id(2)] NeuronId NeuronId)
{
    public string ToGrainKey() => NeuronAddressKeys.Create(this);
}
```

`NeuronAddress` is the durable logical identity. Its key must not contain:

- MCP versus UI audience
- access token
- refresh token
- transport session ID
- Flutter route
- server replica
- FeatureHost worker ID
- current model

Actor-private Neurons include the actor in `SpaceId`. Shared Owner Neurons use an Owner space. Session Neurons are intentionally keyed by their session identity because the session itself is the Neuron.

Examples:

```text
owner/local-owner/actor/flutter-ui/chat/main
owner/local-owner/actor/flutter-ui/ui/ask
owner/local-owner/actor/flutter-ui/ui/activity
owner/local-owner/connection/google-primary
owner/local-owner/feature/inbox-triage
owner/local-owner/effect/01J...
session/4df...
system/login/local-development
```

Orleans grain identity is grain type plus grain key. DigitalBrain therefore uses one stable grain type and the exact same `NeuronAddress.ToGrainKey()` from MCP and UI callers.

## 4. Universal grain contract

```csharp
[Alias("digitalbrain.neuron.v1")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("describe")]
    Task<NeuronDescription> DescribeAsync();

    [Alias("read")]
    Task<NeuronSnapshot> ReadAsync(NeuronRead request);

    [Alias("invoke")]
    Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation);

    [Alias("events")]
    Task<NeuronEventPage> ReadEventsAsync(NeuronEventCursor cursor);
}
```

The base contract deliberately has four operations:

| Operation | Responsibility |
|---|---|
| `DescribeAsync` | Return kind, schemas, facets, actions, and observation metadata |
| `ReadAsync` | Return a bounded projection of the current Neuron revision |
| `InvokeAsync` | Execute one authenticated, idempotent, typed contract |
| `ReadEventsAsync` | Recover durable events after a cursor |

The base contract does not contain Gmail, login, Feature, or UI-specific methods.

## 5. State envelope

```csharp
[GenerateSerializer, Alias("digitalbrain.neuron-document.v1")]
public sealed record NeuronDocument(
    [property: Id(0)] NeuronAddress Address,
    [property: Id(1)] string Kind,
    [property: Id(2)] int SchemaVersion,
    [property: Id(3)] long Revision,
    [property: Id(4)] NeuronLifecycle Lifecycle,
    [property: Id(5)] NeuronFacetDescriptor[] Facets,
    [property: Id(6)] byte[] StatePayload,
    [property: Id(7)] SynapseRecord[] Synapses,
    [property: Id(8)] NeuronEventRecord[] EventTail);
```

The envelope is universal. `StatePayload` is serialized through the registered kind/facet schema and remains strongly validated. The kernel must reject:

- unknown kinds
- unknown facet contracts
- unbounded payloads
- schema mismatches
- revision regressions
- duplicate event IDs
- invalid Synapse relations
- product state whose embedded identity differs from the grain key

The existing encrypted runtime-state envelope remains valuable and becomes the storage mechanism for `NeuronDocument`. Per-kind allowlists in `EncryptedPersistentState` collapse into one Neuron aggregate kind.

## 6. Typed facets

```csharp
public interface INeuronFacet;

public interface INeuronFacet<TCommand, TResult> : INeuronFacet
{
    ValueTask<TResult> InvokeAsync(
        NeuronExecutionContext context,
        TCommand command,
        CancellationToken cancellationToken);
}
```

Domain interfaces remain expressive:

```csharp
public interface IGmail : INeuronContract
{
    Task<GmailMessagePage> ReadMessagesAsync(ReadGmailMessages request);
    Task<NeuronReference> ProposeSendAsync(ProposeGmailSend request);
}

public interface ILogin : INeuronContract
{
    Task<SessionReference> AuthenticateAsync(LoginRequest request);
}

public interface IUiNeuron : INeuronContract
{
    Task<UiNeuronProjection> ProjectAsync(UiProjectionRequest request);
}
```

The long-term generator produces:

- stable contract IDs
- serializers
- JSON schemas
- typed `NeuronClient<TContract>` proxies
- handler registration
- MCP input schemas
- Flutter action schemas
- conformance tests

The first implementation slice may register facets manually. It must use the final public types so manual registration can later be deleted without changing consumers.

## 7. Technical traits

Every Neuron declares one or more traits:

| Trait | Runtime behavior |
|---|---|
| `StateFacet` | Durable revisioned state |
| `CommandFacet` | Typed idempotent invocation |
| `QueryFacet` | Read-only typed invocation |
| `ProjectionFacet` | Creates a bounded UI or activity projection |
| `ActionFacet` | Publishes signed, revision-bound actions |
| `WorkFacet` | Inbox, lease, fence, retry, completion, pause |
| `ScheduleFacet` | Durable reminder-backed scheduling |
| `ModelFacet` | Bounded model execution with explicit budget |
| `ConnectionFacet` | OAuth lifecycle and connection health |
| `EffectFacet` | Prepared external mutation |
| `DecisionFacet` | Human approval or decline |
| `ModuleFacet` | Approved programmable behavior |
| `ObservationFacet` | Durable cursor and live progress stream |

Examples:

| Neuron | Traits |
|---|---|
| Login | Command, Projection |
| Session | State, Command |
| Chat | State, Command, Work, optional Model, Projection, Action |
| Feature | State, Command, Work, Schedule, Module, Projection |
| Gmail | Query, Connection, optional Model |
| Gmail send Effect | State, Effect, Decision, Projection, Action |
| Activity UI | Projection, Observation |
| Flutter Ask destination | Projection, Action, Observation |

No trait creates another runtime.

## 8. Synapses

```csharp
public enum SynapseRelation
{
    Contains,
    Requires,
    Grants,
    BackedBy,
    Projects,
    CausedBy,
    Awaits,
    Approves,
    EmitsTo,
    UsesModule
}
```

```csharp
[GenerateSerializer, Alias("digitalbrain.synapse.v1")]
public sealed record SynapseRecord(
    [property: Id(0)] string SynapseId,
    [property: Id(1)] NeuronAddress Source,
    [property: Id(2)] SynapseRelation Relation,
    [property: Id(3)] NeuronAddress Target,
    [property: Id(4)] long Revision,
    [property: Id(5)] string ConstraintsJson,
    [property: Id(6)] NeuronAuthority Authority);
```

Initial valid relationships:

```text
RootUi --Contains--> AskUi
RootUi --Contains--> ActivityUi
AskUi --Projects--> Chat
ActivityUi --Projects--> ChatOperation
Feature --Requires--> Gmail
Feature --Grants--> Gmail
Gmail --BackedBy--> GoogleConnection
ChatOperation --CausedBy--> Chat
ChatOperation --Awaits--> Effect
Approval --Approves--> Effect
Effect --EmitsTo--> ActivityUi
Feature --UsesModule--> FeatureRelease
```

There is no general graph language, graph marketplace, or arbitrary tenant relation editor in the first implementation.

## 9. Invocation pipeline

Every command follows this pipeline:

```text
1. Authenticate caller transport.
2. Resolve caller to Owner, Actor, Session Neuron, assurance, and grants.
3. Resolve the exact target NeuronAddress.
4. Load and validate NeuronDocument.
5. Validate command ID and idempotency replay.
6. Validate expected revision when supplied.
7. Resolve the typed facet contract.
8. Evaluate caller grants and relevant Synapses.
9. Invoke the facet handler.
10. Convert external mutation requests into Effect Neurons.
11. Append Neuron events and advance exactly one revision.
12. Persist before acknowledging success.
13. Project affected UI Neurons.
14. Append SurfaceFeed Neuron records.
15. Publish optional progress/observation events.
```

Shared Orleans incoming call filters may carry logging, correlation, and coarse transport metadata. Product authorization remains explicit in the Neuron invocation pipeline.

## 10. UI is Neurons

Flutter does not own a parallel application model. It renders and invokes UI Neurons.

```csharp
[GenerateSerializer, Alias("digitalbrain.ui-neuron-projection.v1")]
public sealed record UiNeuronProjection(
    [property: Id(0)] NeuronAddress Address,
    [property: Id(1)] long Revision,
    [property: Id(2)] string ViewKind,
    [property: Id(3)] JsonElement Data,
    [property: Id(4)] UiNeuronAction[] Actions,
    [property: Id(5)] NeuronReference[] Children);
```

The initial UI graph is:

```text
Root UI Neuron
├── Login UI Neuron
├── Ask UI Neuron
├── Features UI Neuron
├── Activity UI Neuron
└── Connections UI Neuron
```

Each destination has a stable NeuronAddress. A route is only navigation to that address.

`SurfaceFeedNeuron` remains the first observation implementation. Its records must identify the projected UI Neuron and the causal domain Neuron. The existing surface action token remains useful, but its binding evolves from:

```text
surface ID + action type
```

to:

```text
target NeuronAddress + contract ID + target revision + action schema
```

RFW is deleted. Flutter keeps a closed native `ViewKind` registry.

## 11. MCP

The target public tools are:

```text
neuron_describe(address)
neuron_read(address, projection)
neuron_invoke(address, contract, input, commandId, expectedRevision?)
```

Optional observation is provided as an MCP resource or bounded read cursor rather than one tool per product noun.

Compatibility mapping:

```text
ino_interact
    -> neuron_invoke(ChatNeuron, "digitalbrain.chat.interact.v1", ...)

feature_pause
    -> neuron_invoke(FeatureNeuron, "digitalbrain.lifecycle.pause.v1", ...)

feature_resume
    -> neuron_invoke(FeatureNeuron, "digitalbrain.lifecycle.resume.v1", ...)

feature_inspect
    -> neuron_read(FeatureNeuron, "digitalbrain.feature.summary.v1")
```

Aliases are deleted after callers and tests move to the universal tools.

MCP transport sessions authenticate callers. They do not contain product state. MCP and Flutter callers resolving the same NeuronAddress therefore mutate and observe the same Neuron.

## 12. Effects and approvals

Every external mutation produces an Effect Neuron:

```text
Effect Neuron
  proposed payload digest
  connector Neuron
  provider idempotency key
  requesting Neuron
  requesting Actor
  decision state
  execution fence
  verified outcome
```

An Approval Neuron creates an `Approves` Synapse to the exact Effect revision and payload digest.

Connector mutation handlers accept:

```csharp
public sealed record ApprovedEffectProof(
    NeuronAddress Effect,
    long EffectRevision,
    string PayloadDigest,
    string DecisionId,
    string ExecutionFence,
    DateTimeOffset ApprovedAt);
```

No connector mutation overload may accept only raw provider arguments.

## 13. Features

A Feature is a Neuron with `ModuleFacet` and `WorkFacet`.

The current concepts map as:

| Current concept | Universal representation |
|---|---|
| Feature hub | Owner Feature index UI/projection Neuron |
| Feature installation | Owner-scoped Feature Neuron |
| Feature release | Immutable module artifact Neuron |
| Feature grant | `Grants` Synapse |
| Required capability | `Requires` Synapse |
| Publication | `UsesModule` Synapse switch |
| Inbox | `WorkFacet` |
| Lease/fence | `WorkFacet` |
| Completion | Run Neuron terminal state |
| Intent | Child Operation or Effect Neuron |
| Run projection | Activity UI Neuron projection |
| Pause/resume | Universal lifecycle contracts |

FeatureHost becomes a generic Neuron module worker. FeatureBuilder becomes an optional module compiler. Neither owns product identity or approval.

## 14. Execution profiles

```csharp
public enum NeuronExecutionProfile
{
    Deterministic,
    ConnectorBacked,
    ModelBacked,
    Composite,
    ProjectionOnly
}
```

- Login is deterministic.
- Session is deterministic and persistent.
- Gmail typed reads are connector-backed.
- Gmail natural-language behavior may be composite.
- Chat may be model-backed or deterministic depending on the invoked contract.
- UI Neurons are projection-only plus actions.
- Effect and Approval Neurons are deterministic and persistent.

The execution profile selects handlers. It never changes identity or transport.

## 15. Reliability

The kernel owns:

- bounded command IDs
- idempotent replay receipts
- expected revision checks
- exactly-one revision advancement
- deterministic event IDs
- lease fences
- retry policy
- pause state
- outcome-unknown state
- event cursor recovery
- state schema validation
- projection retry
- action token renewal

Existing Feature, Conversation, Effect, Session, and Surface implementations contain valuable versions of these mechanics. Migration extracts them into kernel policies before deleting the specialized copies.

## 16. Failure behavior

| Failure | Required behavior |
|---|---|
| Duplicate command | Return the original receipt without re-execution |
| Wrong expected revision | Fail with bounded conflict information |
| Unknown contract | Fail closed without state change |
| Missing grant Synapse | Fail closed without handler invocation |
| Unhealthy connection | Return unavailable and project connection health |
| External timeout | Effect becomes outcome-unknown; never blindly retry mutation |
| Flutter offline | Durable UI/feed revisions catch up from cursor |
| MCP disconnect | Accepted command continues under Neuron work lease |
| Expired action binding | UI Neuron re-projects a fresh revision-bound binding |
| Projection failure | Domain commit remains durable; projection retries idempotently |
| Invalid persisted state | Activation fails closed |

## 17. Existing code to retain initially

- `NeuronId`
- `NeuronScope`
- `RequestContext`
- `RequestScope`
- encrypted runtime-state protection
- `SessionTokenService`
- `SessionNeuron`
- `ConversationNeuron`
- `SurfaceFeedNeuron`
- `InoEffectPlanNeuron`
- current Feature grains and transitions
- Google and Salesforce provider adapters
- current Surface action capability tokens
- UI gRPC feed and Flutter feed cursor handling
- Aspire AppHost resource topology

These are migration inputs, not permanent evidence that specialized runtimes must remain.

## 18. Code targeted for collapse

- specialized Feature lifecycle orchestration
- duplicated Feature/INO leases and work queues
- capability catalogs and resolver layers whose data can come from Neuron descriptions and Synapses
- feature-specific MCP tools
- feature-specific UI RPCs and generated DTOs
- separate activity query plumbing
- RFW host, protocol payload, libraries, and tests
- product-specific action authorization branches
- per-aggregate runtime-state kind allowlists and storage resources
- duplicated approval representations
- transport-specific product state

## 19. Deletion target

Measured baseline:

| Area | Lines |
|---|---:|
| Backend production | 36,751 |
| Flutter handwritten | 30,033 |
| Flutter generated protobuf | 7,796 |
| Tests | 49,777 |
| Kernel Feature runtime | 6,632 |
| FeatureHost + FeatureBuilder | 3,624 |
| MCP project | 7,942 |
| Flutter Studio | 7,323 |
| Flutter runtime | 6,836 |
| RFW | 4,756 |

Expected target:

- 70–80% deletion in transport, orchestration, generated DTOs, Feature lifecycle duplication, RFW, and redundant tests
- 45–60% deletion across production source overall
- zero new parallel runtime

Every migration phase records lines added, lines removed, and specialized concepts deleted.

## 20. First vertical slice

The first slice does not attempt the full migration. It establishes the invariant through the existing live spine:

1. Represent Chat and Ask UI with explicit Neuron addresses.
2. Attach the target UI Neuron identity to surface projections.
3. Ensure expired or consumed action bindings are renewed.
4. Add a development-only MCP session path using the same Owner/Actor identity as Flutter.
5. Invoke the Chat Neuron through DigitalBrain MCP.
6. Observe the resulting Chat/Operation state through the SurfaceFeed Neuron.
7. Verify Flutter receives the new UI Neuron projection.
8. Record the causal chain:

```text
MCP Session Neuron
    -> Chat Neuron invocation
    -> Operation Neuron
    -> Ask UI Neuron projection
    -> SurfaceFeed Neuron
    -> Flutter
```

## 21. First-slice acceptance criteria

1. `aspire doctor` passes.
2. Kernels, MCP, FeatureHost, and Flutter resources are healthy.
3. A UI session and MCP session resolve to the same Owner and Actor.
4. The Ask UI projection has a stable NeuronAddress.
5. The initial Flutter feed includes a valid Chat action.
6. DigitalBrain MCP lists `ino_interact` or its `neuron_invoke` alias.
7. Calling the tool returns an accepted durable operation.
8. Conversation/Operation state revision advances.
9. SurfaceFeed Neuron sequence advances.
10. Ask or Activity UI Neuron revision advances.
11. Flutter receives the new projection without restarting.
12. The projection cause identifies the MCP-created operation.
13. Replaying the same command ID does not create a second operation.
14. Flutter reconnect catches up if it was offline during invocation.
15. No external mutation or approval bypass occurs.

## 22. Full migration completion criteria

- one public `INeuron` Orleans grain interface
- typed facets over the universal kernel
- one generic Neuron invocation envelope
- one generic Neuron projection envelope
- one generic Flutter gateway/controller path
- three public MCP Neuron tools
- grants and approvals expressed through Neurons and Synapses
- Feature work running through universal `WorkFacet`
- RFW deleted
- feature-specific RPCs deleted
- specialized grain interfaces deleted or retained only as temporary adapters
- exact root test command passes with zero skips
- live MCP-to-Flutter proof passes
- deletion metrics meet the approved phase targets

## 23. Open implementation decisions

Only these remain open during implementation:

1. Whether the first `INeuron` implementation wraps specialized grains or directly owns Chat state.
2. Whether `NeuronAddress.SpaceId` reuses `NeuronScope` text or introduces a separately serialized scope type.
3. Whether the first typed proxy is hand-written or source-generated.
4. Whether observation initially remains gRPC-only or also appears as an MCP resource.
5. Whether the universal storage provider is introduced before or after the Chat slice.

The first slice chooses the option with the smallest reversible change and the fastest live proof.

## 24. Documentation grounding

- Orleans grains provide stable type-and-key identities, managed activation, persistence, reminders, call filters, request context, and streams.
- Persistent state remains injected through `IPersistentState<TState>`.
- MCP remains a thin authenticated transport over Neuron invocations.
- Flutter integration tests verify streamed state and rendered projections.
- Aspire controls the running distributed topology, targeted resource rebuilds, health, logs, and traces.

Context7 was invoked for Orleans, MCP C# SDK, and Flutter documentation. The configured Context7 account reported its monthly quota exhausted, so implementation must use official Microsoft, MCP C# SDK, Flutter, and Aspire documentation as the temporary source of current API truth.
