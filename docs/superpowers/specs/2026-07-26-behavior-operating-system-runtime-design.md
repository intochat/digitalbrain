# Behavior operating system and runtime design

**Date:** 2026-07-26

**Status:** Approved design; runtime rail remains unbuilt

**Decision owner:** project owner

**Supersedes:** post-rail Behavior identity and execution decisions in
`2026-07-25-behavior-os-design.md` and its residual grills. Those documents remain evidence of the
current pre-rail implementation. Where they conflict with this document, this document wins.

## 1. Decision

DigitalBrain has three deliberately different layers:

- **Framework:** neurons, synapses, journals, delivery, ownership, and the generic machinery that
  hosts Behaviors.
- **Modules:** separately packaged, compile-time neuron contracts and synapse vocabulary. A large
  ecosystem may contain hundreds of first-party and community modules.
- **Operating system:** installed Behaviors that compose module vocabulary into user-visible
  outcomes, including UI policy.

A Behavior is represented at runtime by an owner-scoped `BehaviorNeuron`. Its approved program is
an immutable single-file C# artifact executed by a replaceable executor. The program is not a
Neuron, does not inherit `Neuron`, and never receives Orleans authority.

The load-bearing distinction is:

```text
BehaviorNeuron = identity + journal + state + authority + revision lifecycle
Behavior program = approved logic evaluated on behalf of that neuron
```

One Orleans grain implementation hosts all Behavior instances. Adding or revising a Behavior does
not add a new CLR grain type to the running cluster.

## 2. Why this model

The model keeps the original claim that everything durable and operational is a neuron without
mistaking user-authored code for trusted framework machinery. A Behavior has neuron identity and
journals because `BehaviorNeuron : Neuron, IBehavior`; its program remains data owned by that
neuron.

.NET file-based applications are ordinary compiled applications backed by a virtual project, not
interpreted scripts. They are a good one-file authoring format, but they are not a security
boundary. `AssemblyLoadContext` isolates dependency loading and type identity, not privileges.
Modern .NET does not provide Code Access Security or an AppDomain sandbox; untrusted code requires
an operating-system process or container boundary.

The primary-source findings and links are recorded in
[`../../research/2026-07-26-dotnet-file-based-apps-for-behaviors.md`](../../research/2026-07-26-dotnet-file-based-apps-for-behaviors.md).

## 3. Terms and identity

| Term | Meaning |
| --- | --- |
| `BehaviorId` | Stable logical identity chosen when a Behavior is first defined |
| `BehaviorRevisionId` | Content hash of an immutable source, manifest, tests, dependency lock, and compiler policy |
| Behavior definition | Shareable source, metadata, schemas, tests, and provenance; contains no owner state |
| Behavior installation | Owner-scoped approval and configuration of a definition |
| Behavior instance | The `BehaviorNeuron` addressed by `(OwnerId, BehaviorId)` |
| Behavior execution | One causally identified response to an event or intent |
| Program | The single-file C# logic belonging to a revision; never a grain |

Community sharing operates on definitions. Installation, approval, active revision, state, grants,
and history are always scoped to the owner. Revisions are immutable; changing source or policy
creates a new `BehaviorRevisionId`. Rollback atomically selects an older approved revision rather
than mutating it.

## 4. Entry points

Every installed revision may expose both kinds of entry point.

### 4.1 Event subscriptions

A Behavior subscribes to one or more compiled synapse contracts. The compiler derives subscriptions
from program interfaces and records their stable wire aliases in the manifest. Delivery is a
broadcast routing concern, not a distinct kind of Behavior.

The subscription registry maps:

```text
(OwnerId, SynapseAlias) → installed Behavior addresses
```

Installation and rollback replace a Behavior's complete subscription set atomically. Uninstall
removes it atomically. Runtime routing uses the stable synapse alias, never `Type.FullName`.

### 4.2 Intent invocation

An assistant or application may invoke an installed Behavior through an exact catalog address:

```text
(OwnerId, BehaviorId, IntentSchemaId, IntentSchemaVersion)
```

The request and result payloads are validated against the approved schemas before they cross the
Behavior boundary. This is not arbitrary `Run("some name")` dispatch: discovery may be semantic,
but authorization resolves to an exact installation, active revision, schema, and grant set.

Intent submission returns a durable `ExecutionId` receipt immediately. The client or assistant
observes or queries the correlated outcome through the owner-scoped client facade. It does not keep
an Orleans grain turn or an in-memory promise open while the worker runs.

Behavior-specific intent and result shapes do not become public CLR neuron contracts. They remain
versioned schemas owned by the Behavior definition. Only modules add public CLR vocabulary.

## 5. Program model

The proposed SDK surface is intentionally small:

```csharp
public interface IBehaviorProgram<in TTrigger>
    where TTrigger : Synapse
{
    ValueTask ExecuteAsync(
        TTrigger trigger,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}

public interface IIntentProgram<TRequest, TResponse>
{
    ValueTask<TResponse> ExecuteAsync(
        TRequest request,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}
```

These are program contracts, not neuron contracts. A file may implement several event and intent
contracts and may contain private helper types, but the revision still has one source file and one
stable `BehaviorId`.

An illustrative boot Behavior is:

```csharp
#:property TargetFramework=net10.0
#:property OutputType=Library
#:property PublishAot=false
#:package DigitalBrain.BehaviorSdk@1.0.0
#:package DigitalBrain.Modules.Flutter.Contracts@1.0.0

public sealed class StartUiProgram
    : IBehaviorProgram<DigitalBrainActivated>
{
    public async ValueTask ExecuteAsync(
        DigitalBrainActivated trigger,
        IBehaviorContext brain,
        CancellationToken cancellationToken)
    {
        var shell = brain.Get<IShell>("desk");
        var command = brain.DeterministicCommandId("open-login");

        await shell.Open(
            new OpenScene(command, "login", "Sign in"));
    }
}
```

The directive header is system-generated from the manifest. Proposed source may not choose feeds,
packages, SDKs, build targets, compiler properties, or wildcard versions.

`IBehaviorContext` is the program's only path to DigitalBrain. It exposes:

- Proxies for exact approved module neuron contracts.
- Behavior-private state reads and proposed state transitions.
- Deterministic execution time and identifiers.
- Cancellation and execution metadata.

It does not expose `IGrainFactory`, `IServiceProvider`, `IChatClient`, provider SDKs, raw MCP,
`HttpClient`, filesystem/process APIs, reflection, native interop, ambient time, or ambient random.

## 6. Revision pipeline

An AI or human proposal passes through the same journaled pipeline:

1. Produce one `.cs` file, manifest, event subscriptions, intent/result schemas, requested
   capabilities, and BDD scenarios.
2. Resolve every module and Behavior candidate through the exact compiled catalogs. Vector search
   supplies candidates only.
3. Generate the canonical directive header and exact dependency lock.
4. Compile in an isolated build worker using the .NET file-based application build model.
5. Run syntax, reference, API, and policy analyzers.
6. Run the revision's BDD suite in a controlled test brain.
7. Store the source, manifest, tests, results, artifact, policy version, and hashes as one immutable
   revision.
8. Present that exact revision to the owner.
9. Install only after explicit human approval of the revision hash and requested grants.
10. Atomically select the revision and replace its subscriptions.

Production executes the exact artifact that passed tests and approval. It never restores or
recompiles source during an invocation.

### Build-worker boundary

The build worker is treated as untrusted because file directives, MSBuild, restore, analyzers, and
NuGet package build targets can execute code.

It receives only the proposal and approved contract packages. It has no secrets, source repository,
Orleans credentials, or general network access. Dependencies come from a pre-populated,
allowlisted, exact-version package source. Only contract-only packages approved by DigitalBrain may
participate; arbitrary community NuGet packages are not admissible merely because their public API
looks safe.

Behavior artifacts are libraries loaded by the runtime worker. Native AOT is disabled for these
libraries; .NET single-file deployment may package the worker host, but deployment bundling is
unrelated to Behavior isolation.

## 7. Runtime execution

`BehaviorNeuron` performs only short, durable turns:

1. Accept an event delivery or validated intent.
2. Resolve the active approved revision.
3. Commit `BehaviorExecutionStarted` with a unique `ExecutionId`, trigger lineage, revision, and
   input fingerprint.
4. Enqueue execution through the durable outbox and return from the grain turn.
5. Accept a correlated completion, failure, or timeout later.
6. Commit approved private-state transitions and `BehaviorExecutionCompleted` or
   `BehaviorExecutionFailed`.
7. Make an intent outcome available by `ExecutionId` or emit configured generic lifecycle facts.

The grain never holds a turn open while arbitrary code executes.

The runtime worker receives only the approved assembly, serialized input, deadline, resource
limits, and a scoped IPC connection. It receives no Orleans client or direct infrastructure
credentials.

### Capability broker

All program calls pass through a trusted broker. A call is admitted only when all of these match:

- Owner, Behavior, execution, and revision.
- Declared module contract and method.
- Target neuron identity.
- Approved capability grant.
- Argument schema and request fingerprint.
- Causal execution lineage.

The broker then invokes the real module neuron. Module operations keep their own journals and
domain-specific recovery rules.

Each broker call has a deterministic `(ExecutionId, CallOrdinal)` identity. The first runtime
version permits only sequential capability calls, enforced by the SDK and analyzer, so ordinal
assignment is stable. Ambient time and randomness are forbidden; deterministic values come from
the context. The broker records each request fingerprint and result. If an execution is replayed
after worker loss, an identical call receives the recorded result; a different request at the same
ordinal fails rather than performing an ambiguous second effect. Explicit stable call identities
may later enable parallel calls without weakening replay. Provider uncertainty remains the owning
module's responsibility—this mechanism does not claim exactly-once external effects.

Behavior-private state is read through the context and committed by `BehaviorNeuron` only after a
valid execution transition. A worker cannot write grain storage directly.

## 8. Isolation

Unknown AI and community revisions execute out of process by default.

The Windows-first runtime combines:

- AppContainer or LPAC for privilege isolation.
- A non-breakaway Job Object for process-tree, CPU, memory, and termination limits.
- An explicitly ACL-restricted named pipe for broker IPC.
- No network capability and a read-only view of the selected artifact.
- Deadline enforcement that terminates the entire job, not only a cancellation token.

A Job Object is resource supervision, not a privilege sandbox. Hostile multi-tenant execution uses a
Hyper-V-isolated container or stronger boundary. Equivalent non-Windows isolation must be designed
and proven before that platform is supported.

Minimal source-controlled, signed boot and recovery revisions may use a trusted in-process executor
so the operating system can recover its worker infrastructure. They retain the same revision,
manifest, context, broker, journal, and BDD model. `AssemblyLoadContext` is dependency isolation
only. Provenance and policy select the executor; they do not create a second kind of Behavior.

## 9. AI discovery and composition

The assistant searches two derived indexes:

- Compiled module CLR contracts and synapse aliases.
- Installed Behavior manifests, intent schemas, descriptions, examples, and grants.

Embeddings rank candidates. They never grant authority and never determine a runtime type by
similarity. The assistant must resolve a result back to an exact catalog record before it can invoke
or reference it.

The assistant may:

- Invoke already installed and approved Behavior intents.
- Cause event-driven Behaviors indirectly by calling approved module contracts.
- Compose a new source file, manifest, schemas, and BDD scenarios.
- Submit a revision proposal and explain requested grants.

It may not approve, install, replace, widen grants, or activate a new revision. Those transitions
require the owner.

## 10. BDD and evidence

BDD is part of the revision, not documentation beside it. Approval is impossible unless all
scenarios for the exact artifact are green.

Minimum system scenarios include:

```gherkin
Given DigitalBrain is activated for an owner
And the approved StartUi Behavior subscribes to DigitalBrainActivated
When activation is committed
Then the Behavior journal records the execution
And IShell receives OpenScene for the first screen
And SceneOpened is committed
And Flutter renders from SceneOpened
```

```gherkin
Given an assistant resolves an installed Behavior intent
When its request matches the approved input schema and grants
Then the exact active revision executes
And the result matches the approved output schema
```

```gherkin
Given a worker dies after a capability call completes
When the same execution is recovered
Then the broker returns the recorded call result
And the module effect is not invoked a second time
```

```gherkin
Given a proposed revision passed compilation and BDD
When any source, dependency, manifest, policy, or test input changes
Then its revision hash changes
And the previous approval cannot install it
```

Product proofs assert journals and observable module/edge outcomes. Private-field assertions,
compile-only checks, and mocked grain substitutes do not prove a Behavior works.

## 11. Package and ownership target

The exact project names may be simplified during implementation, but these ownership boundaries are
fixed:

| Home | Owns |
| --- | --- |
| `DigitalBrain.Abstractions` | `IBehavior`, stable IDs, generic lifecycle and intent envelopes |
| `DigitalBrain.Kernel` | `BehaviorNeuron`, journaling, revision activation, subscription routing |
| Behavior SDK | program interfaces, safe context surface, manifest/schema contracts |
| Behavior compiler host | isolated restore, compilation, analysis, artifact production |
| Behavior worker host | isolated artifact loading and program execution |
| OS source tree/package | minimal built-in Behavior sources, manifests, and features |
| Modules | compile-time public neuron/synapse vocabulary and runtime implementations |
| Edges | authentication, transport, projections, and pixels; never OS policy |

The Behavior SDK and hosts are framework machinery. The installed revision set is the operating
system.

## 12. Required migration and deletion

Implementation must end with one product path:

| Current artifact | Required outcome |
| --- | --- |
| Flutter-owned `OpenHomeOnActivationBehavior` | Replace with OS-owned `StartUi` revision; Flutter keeps only rendering vocabulary/runtime |
| `ActivateDigitalBrain`, `BootOnActivation`, `OpenHome`, `PostAuthBootstrap`, and overlapping surface helpers | Fold useful logic into Behaviors, preserve BDD outcomes, then delete the redundant pull path |
| Compiled `IAccountEnrichment` process neuron and module capsule | Re-express as a Behavior over Google and Salesforce module contracts; retain private history in `BehaviorNeuron`; delete the sample module after parity |
| `DigitalBrainClient.RequireDomainNeuronContract` blanket rejection of `IBehavior` | Replace with explicit routing rules that admit exact Behavior addresses and intent envelopes |
| `SubscriptionRegistry` keyed by CLR full name | Move to stable aliases, owner scope, atomic revision replacement, and uninstall |
| Source-generated-only private dispatch seam | Add the smallest protected generic dispatch seam needed by `BehaviorNeuron`, preserving base delivery, journal, dedupe, and outbox invariants |
| Documents that call the new model absent, forbid a rail package, or identify Behavior identity with a concrete grain class | Mark historical or update to this approved design; continue to label the rail unbuilt until tests prove otherwise |

No legacy route is retained “just in case.” Git is the recovery mechanism. Deletion happens only
after the replacement product sentence is green at the root gate.

## 13. Repository completion standard

The refactor is not complete merely when the new projects build. Completion requires:

- One activation-to-UI product path.
- One account-enrichment product path.
- No Behavior logic inside module implementations.
- No module vocabulary invented by a Behavior.
- No runtime restore or compilation during an invocation.
- No direct Orleans or infrastructure authority in a program.
- No stale project references, empty folders, checked-in build output, commented-out code, temporary
  artifacts, obsolete samples, duplicate docs, or contradictory status claims.
- Architecture, package, hosting, testing, and contributor docs describing the code that actually
  exists, with Designed and Built explicitly separated.
- Repository searches for retired type/project names returning only intentional migration history.
- Formatting, analyzers, documentation checks, root Release build, and unfiltered root Release tests
  green.

## 14. Rejected alternatives

### Execute `dotnet behavior.cs` on every trigger

Rejected because it couples restore, build, cache invalidation, and execution to the product path.
It also exposes file directives and implicit MSBuild/NuGet inputs at invocation time.

### Load community assemblies inside the silo

Rejected because dependency isolation is not privilege isolation. A crash, loop, memory leak, or
forbidden API would share the authority and availability boundary of the brain.

### Generate one grain class per Behavior

Rejected because installed community logic would require adding CLR grain implementations to a
running cluster. Orleans already provides the correct identity model: one grain implementation with
many keyed instances.

### Make every Behavior a module

Rejected because it collapses logic back into vocabulary, forces rebuilds for user policy, and
prevents owner-scoped installation and revision history.

### Let Behaviors add public CLR intent contracts

Rejected because runtime installation cannot safely change the compiled type universe. Versioned
schemas keep Behavior-local vocabulary dynamic while module contracts remain typed and compiled.

## 15. Strongest counterargument

The recommended design introduces two out-of-process systems, artifact storage, IPC, sandbox
launching, capability mediation, and replay bookkeeping. An in-process compiler and
`AssemblyLoadContext` would be much smaller.

That simpler design is valid only for trusted source-controlled built-ins. It cannot safely support
the approved product claim that an AI and a community may contribute executable Behaviors. The
complexity is therefore accepted at one narrow boundary—the compiler/executor seam—while the rest of
the system remains ordinary neurons, synapses, module contracts, and BDD.

## 16. Ratified invariants

1. Framework equals neuron/synapse mechanics; the installed Behavior set is the operating system.
2. Modules alone add public CLR neuron and synapse vocabulary.
3. `BehaviorNeuron : Neuron, IBehavior`; the single-file program is not a Neuron.
4. Behavior identity is `(OwnerId, BehaviorId)` with immutable approved revisions.
5. One registered Behavior grain implementation hosts all Behavior instances.
6. Event subscriptions and schema-validated intent invocation are equally valid entry points.
7. Broadcast versus directed delivery is routing, not a Behavior taxonomy.
8. Vector search discovers candidates; exact catalogs and grants authorize them.
9. AI may invoke approved Behaviors and propose new ones; humans alone approve installation.
10. Programs execute through a constrained context and trusted capability broker.
11. Unknown code executes outside the silo; single-file source and single-file deployment are not
    security boundaries.
12. Built-ins use the same artifact, revision, journal, capability, and BDD model; only provenance
    may select a trusted executor.
13. The tested and approved artifact hash is the artifact that executes.
14. The migration deletes the dual legacy paths and leaves documentation synchronized with reality.
