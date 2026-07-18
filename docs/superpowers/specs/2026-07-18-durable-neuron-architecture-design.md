# DigitalBrain Durable Neuron Architecture

**Status:** approved design  
**Date:** 2026-07-18  
**Scope:** kernel contracts, durable state, capability discovery, provider ownership, streams, security, Aspire hosting, and migration  
**Supersedes:** the kernel, provider, persistence, discovery, edge-routing, and migration decisions in `2026-07-16-brain-v2-neuron-os-design.md` wherever they conflict with this document. The earlier UI and workspace proposals are outside this design's scope.

## 1. Outcome

DigitalBrain is a type-safe operating system built from durable Orleans capability grains.

Each public capability is a real Orleans grain interface:

```csharp
public interface INeuron : IGrainWithStringKey;

public interface IGmail : INeuron;
public interface ISalesforce : INeuron;
```

Each implementation derives from the official Orleans Journaling base:

```csharp
public abstract class Neuron : Orleans.Journaling.DurableGrain;

public sealed class GmailNeuron : Neuron, IGmail;
public sealed class SalesforceNeuron : Neuron, ISalesforce;
```

The provider is identified by the interface type. The grain key contains only the authenticated owner identity:

```text
typeof(IGmail)      + owner identity → GmailNeuron
typeof(ISalesforce) + owner identity → SalesforceNeuron
```

There is no generic `Kind`, provider prefix, address parser, keyed provider service, proxy dispatcher, or string-based routing catalog.

V1 has one logical neuron instance per owner and leaf capability interface. Supporting multiple accounts for the same provider requires a separately approved typed identity design; V1 does not pre-encode account or connection identifiers into a string key.

## 2. Architectural choice

The selected runtime architecture is a provider-owned Durable Neuron.

Each owner's provider neuron owns:

- DigitalBrain's durable state for that owner and provider.
- Authorization and external-operation state.
- The live provider connection while the grain is active.
- The AI function pipeline and its official runtime tools.
- Recovery of unfinished DigitalBrain work.

Two rejected alternatives are:

1. **Durable grains plus stateless provider executors.** This adds another command protocol, distributes credentials outside the owning actor, separates live tools from the AI loop, and recreates the internal invocation layer this design removes.
2. **A central tool orchestrator.** This centralizes user and provider routing, recreates `Kind` under another name, and becomes a multi-user bottleneck and failure domain.

Remote provider calls can make a single provider grain turn long. This is an accepted v1 trade-off because the grain is isolated to one owner and provider. Execution is not split into another service until measurements demonstrate a need.

## 3. Contract and identity rules

`INeuron` is the common Orleans identity contract. Leaf interfaces such as `IGmail` and `ISalesforce` are the compile-time capability identities.

The client programming entry point mirrors IAW:

```csharp
var gmail = brain.Get<IGmail>();
var salesforce = brain.Get<ISalesforce>();
```

`DigitalBrainClient.Get<TNeuron>() where TNeuron : INeuron` is a typed convenience over `IClusterClient.GetGrain<TNeuron>(authenticatedOwnerId)`.

It is not:

- `DispatchProxy`.
- A service locator.
- A keyed-DI lookup.
- A generic invocation envelope.
- A string-to-provider map.

The authenticated `DigitalBrainClient` supplies the owner identity. Public application code cannot pass an arbitrary owner grain key. The kernel also validates owner access server-side; client-side key binding is not the security boundary.

The following constructs are removed:

- `INeuronKind`.
- `KindCatalog`.
- `NeuronAddress.Kind`.
- Provider-prefixed grain keys.
- String-keyed provider DI.
- Generic string contract dispatch.
- String event-name switches.
- Legacy aliases or compatibility shims for those constructs.

## 4. Durable state ownership

`DurableGrain` is the source of truth for DigitalBrain state. It is not a replacement database for the external provider.

The Durable Neuron is authoritative for:

- Explicit DigitalBrain memory and preferences.
- Authorization policy and decisions.
- External-operation intent and outcome.
- Provider receipts observed by DigitalBrain.
- Connection and recovery status.
- Durable notification delivery state.

The provider remains authoritative for its own records:

- Gmail is authoritative for messages, threads, labels, and drafts.
- Salesforce is authoritative for its CRM records.

Provider records and complete provider responses are not automatically copied into neuron state. Credentials are represented by protected credential references; raw secret values are not written into the journal.

The universal `Neuron` durable state remains small:

- A durable status value.
- A durable external-operation dictionary.
- A durable notification outbox.
- Explicit durable memory required by a behavior.

Derived neurons may add durable collections for their own DigitalBrain behavior, but the base does not become a general-purpose state bag.

State names required by the official Journaling API are derived with `nameof`. Handwritten domain string constants are not used as state identifiers.

## 5. External-operation recovery

An external mutation uses the following durable state machine:

```text
Pending → Succeeded
        → Failed
        → Unknown
```

The execution discipline is:

1. Authorize the operation.
2. Add a typed pending operation to durable state.
3. Await `WriteStateAsync`.
4. Invoke the original external function directly.
5. Persist a typed success, failure, or unknown outcome.
6. Add a typed notification to the durable outbox when required.
7. Await `WriteStateAsync`.

If the first durable write fails, the external operation does not begin.

A crash after the external effect but before the outcome commit is inherently ambiguous. Recovery must not pretend otherwise:

- An idempotent or safely reconcilable operation can be retried or queried.
- A provider receipt can prove success.
- Confirmed absence can permit a policy-controlled retry.
- A non-idempotent, unresolvable operation becomes `Unknown` and requires review.

The application does not build its own event-sourcing system. Orleans Journaling's internal ordered journal reconstitutes its durable values and collections. Application code consumes current durable state, not a home-grown domain event replay API.

Activation restores durable infrastructure state and schedules recovery work. It does not require a remote provider to be available. Remote connections are established lazily. Deactivation only disposes transient resources best-effort; correctness never depends on deactivation running.

## 6. Provider and MCP boundary

Further design of a public typed MCP programming surface is explicitly paused.

This specification does not introduce:

- A generic `Ask` method.
- Generated provider methods.
- An internal `InvokeMcpTool` method.
- Handwritten copies of official provider operations.
- A temporary placeholder API.

When provider integration resumes, it follows the proven IAW runtime direction:

- The provider neuron owns its transient `McpClient`.
- It obtains live `McpClientTool` instances from the official server.
- Those tools enter `ChatOptions.Tools` directly.
- One AI function-invocation middleware provides authorization, durable intent/outcome, redaction, and telemetry.
- The middleware invokes the original function directly.

The following remain transient activation state:

- `McpClient`.
- `McpClientTool` instances.
- Live access credentials.
- Provider response caches.

Official wire names, endpoint URLs, OAuth scopes, and JSON schemas are unavoidable external protocol data. They remain contained at the provider and Aspire boundaries and never become handwritten domain routing identifiers.

The future callable surface of `IGmail` and `ISalesforce` requires a separate approved design before implementation.

## 7. Quadrant capability catalog

Quadrant is a derived, immutable startup catalog. It is not a routing service and does not need durable storage.

A startup task discovers:

- Every public leaf interface assignable to `INeuron`.
- Every non-abstract implementation deriving from `Neuron`.
- The implementation relationship for each leaf interface.

The catalog is keyed by runtime `Type`:

```text
typeof(IGmail)      → typeof(GmailNeuron)
typeof(ISalesforce) → typeof(SalesforceNeuron)
```

Startup fails when:

- A public leaf interface has no implementation.
- More than one implementation claims the same leaf interface.
- An implementation does not derive from `Neuron`.
- A claimed implementation cannot be activated through Orleans.

Orleans remains responsible for grain resolution. Quadrant describes, validates, and reports the installed type system. It does not select an implementation by string.

Presentation metadata can be derived from interface names and generated documentation. Presentation strings are not identity or routing keys.

## 8. Streams and durable outbox

Orleans Streams are notification transport, never the source of truth.

Important delivery follows this order:

1. Change durable neuron state.
2. Add a typed outbox record.
3. Flush the journaled changes.
4. Drain the outbox.
5. Publish a typed notification.
6. Record delivery progress durably.
7. Deduplicate at the consumer using the operation identifier.

A stream failure leaves the outbox item available for retry. Stream loss cannot erase or contradict durable state. A UI or other consumer recovers from durable cursor reads; a stream can provide a low-latency nudge.

Notifications are typed records. Any string identifier required by a transport is derived at the infrastructure boundary from the notification type and is not used for domain dispatch.

## 9. Security

The gateway authenticates people and applications. The kernel validates the authenticated owner against the target neuron.

Security boundaries are:

- The kernel receives journal storage, model configuration, and provider credentials.
- Client applications receive Orleans connectivity and public contracts only.
- Aspire secret resources are not propagated through client references.
- Durable state contains protected credential references, not raw secrets.
- Sensitive provider responses are not persisted by default.
- Logs and traces redact tokens, authorization headers, secrets, and complete private payloads.

One function-invocation middleware applies authorization consistently to official runtime functions. Unknown or uncertain external operations fail closed. External tool annotations can assist policy only when supplied by a configured trusted official provider.

Operational failures are represented by typed states, including:

- `AuthenticationRequired`.
- `AuthorizationDenied`.
- `ProviderUnavailable`.
- `OperationFailed`.
- `OperationUnknown`.
- `StorageUnavailable`.

Provider tool names can appear in secured external-protocol telemetry and durable audit data when needed for reconciliation. They are not internal capability identifiers.

## 10. Aspire `DigitalBrain` resource

Aspire exposes one IAW-style composite resource:

```csharp
var brain = builder.AddDigitalBrain("brain")
    .WithLLM<GptFast>().AsFast()
    .WithLLM<ClaudeBalanced>().AsBalanced()
    .WithLLM<GptReasoning>().AsReasoning()
    .WithEmbedding<TextEmbedding>();

builder.AddProject<Projects.DigitalBrainKernel>("kernel")
    .WithReference(brain);

builder.AddProject<Projects.Api>("api")
    .WithReference(brain.AsClient());
```

`DigitalBrainResource` encapsulates:

- Orleans clustering.
- Official Journaling storage.
- Durable reminders.
- Streams infrastructure.
- Typed Fast, Balanced, and Reasoning model roles.
- Typed embedding configuration.
- Protected credential resources.
- Health checks and telemetry references.

`WithReference(brain)` is the privileged kernel/silo reference. `WithReference(brain.AsClient())` exposes only Orleans client connectivity and public DigitalBrain contracts.

Normal development and production use official durable journal storage. Memory storage is available only in explicitly named tests. The resource does not add Qdrant, blob storage, or other infrastructure without a traced feature requirement.

Model roles are typed. Application code requests a role and does not contain model-name strings. Duplicate or missing required role assignments fail startup.

## 11. Official Journaling implementation gate

The repository currently pins Orleans Core `10.2.1` and Microsoft.Orleans.Journaling `10.2.2-rc.2.alpha.1`. It does not currently reference the official Journaling Azure Storage provider at the repository root.

Implementation begins with a compatibility and restart-recovery spike:

1. Select mutually compatible official Orleans Core, Journaling, and Journaling storage-provider versions.
2. Configure the official provider through Aspire.
3. Persist representative durable values, dictionaries, queues, and lists.
4. Stop the silo completely.
5. Restart it against the same storage.
6. Verify exact recovery and continued writes.
7. Verify failed journal writes prevent subsequent external effects.

If this gate fails, implementation stops. The project does not create a custom journal provider and does not silently fall back to volatile state.

## 12. Migration

This is a clean replacement, not a parallel architecture.

Migration order:

1. Pass the official Journaling compatibility and recovery gate.
2. Introduce minimal `INeuron` leaf contracts and `Neuron : DurableGrain`.
3. Introduce owner-bound `DigitalBrainClient.Get<TNeuron>()`.
4. Add startup-discovered Quadrant validation.
5. Add the durable external-operation ledger and outbox.
6. Make Streams notification-only.
7. Add the Aspire `DigitalBrainResource`.
8. Cut callers over to typed neuron interfaces.
9. Delete the old generic dispatch architecture in the same change series.
10. Resume the MCP programming-surface design separately.

There is no alias period, adapter layer, dual write, dual routing, or legacy `Kind` compatibility map.

## 13. Verification

Required automated gates are:

- Complete silo restart reconstructs every journaled collection.
- Every concrete neuron derives from official `DurableGrain` through `Neuron`.
- Quadrant discovers every public leaf `INeuron` exactly once.
- Missing and duplicate implementations fail startup.
- `brain.Get<IGmail>()` resolves with the authenticated owner identity.
- Cross-owner access fails server-side.
- A failed intent commit prevents an external mutation.
- A crash after an external effect but before outcome persistence recovers as `Unknown` unless reconciliation proves the outcome.
- Outbox delivery is at least once and consumers deduplicate.
- Stream loss does not lose durable state.
- Client projects receive no provider credentials.
- Aspire kernel and client references expose different resource sets.
- Architecture tests reject `INeuronKind`, `KindCatalog`, provider-prefix parsing, `DispatchProxy`, keyed provider DI, and string contract switches.
- The exact root build and test commands pass with no skipped architecture or recovery tests.

## 14. Future programmable operating system

Type-safe interfaces are a primary product capability, not an implementation detail.

The target developer experience is a generated, stable, single-file C# application that:

- References public DigitalBrain contracts.
- Obtains capabilities through `brain.Get<TNeuron>()`.
- Contains no provider-address syntax.
- Contains no provider configuration or routing strings.
- Is independent of provider implementations.
- Uses the C# compiler to reject missing capabilities and invalid API usage.

Plain English is intended to describe behavior and generate stable C# programs. The unresolved provider callable surface must eventually preserve that goal without duplicating official provider code. This specification intentionally does not choose between conversational, generated, or schema-projected provider methods.

## 15. Non-goals

This design does not include:

- A new provider abstraction or invocation protocol.
- A custom event-sourcing system.
- A custom Journaling storage provider.
- Streams as authoritative state.
- A central provider orchestrator.
- Stateless provider executors.
- Provider data replication into neuron state.
- Public typed MCP tool generation.
- A generic `Ask` API.
- Changes to the previously proposed Flutter workspace or visual vocabulary.

## 16. Decision summary

The architectural invariant is:

> A typed provider interface selects one owner-bound Durable Neuron. That neuron is authoritative for DigitalBrain's memory, policy, operation, and delivery state. The official provider remains authoritative for provider records. External functions execute directly; durable middleware records DigitalBrain's commitments around them. Streams only announce committed durable changes.
