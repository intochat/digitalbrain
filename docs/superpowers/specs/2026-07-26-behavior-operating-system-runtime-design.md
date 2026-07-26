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

## Related responsibility records

- [Runtime execution, isolation, discovery, and evidence](2026-07-26-behavior-operating-system-runtime-design-execution-and-isolation.md)
  — sections 7–10; the execution rail remains Designed and unbuilt.
- [Ownership, migration, completion, rejected alternatives, and invariants](2026-07-26-behavior-operating-system-runtime-design-ownership-migration-and-invariants.md)
  — sections 11–16.
