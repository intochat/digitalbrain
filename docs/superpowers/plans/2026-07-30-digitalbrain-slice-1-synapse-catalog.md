# Slice 1: Synapse Client and Exact Capability Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make directed request/result synapses the clean client programming surface and generate an exact active catalog of modules, neurons, accepted/emitted synapses, schemas, descriptions, and versions.

**Architecture:** `brain.Get<TNeuron>()` returns a typed neuron reference with common `SendAsync` plumbing; capability operations are synapse contracts rather than methods on domain neuron interfaces. The source generator emits immutable module capability metadata. `DigitalBrainRuntime` filters it by Aspire-selected modules and exposes the exact active catalog. Slice 2 produces equivalent behavior descriptors and Slice 6 attaches published behavior entries dynamically.

**Tech Stack:** C#/.NET 11, Orleans, Roslyn incremental generator, System.Text.Json schema APIs verified through Context7, xUnit v3, DigitalBrain.Testing.

## Global Constraints

- Read the approved spec and umbrella orchestration plan first.
- Use CodeGraph before edits.
- Do not edit Google, Salesforce, Tasks, Behavior runtime, AI routing, Memory, Flutter, or AppHost files in this slice.
- Preserve current callers through one explicitly temporary compatibility seam; do not retain two permanent client models.
- Keep domain neuron interfaces free of operation-specific methods in the final surface.

---

## Contract End State

```csharp
public abstract record RequestSynapse<TResponse> : Synapse
    where TResponse : Synapse;

public readonly struct NeuronReference<TNeuron>
    where TNeuron : INeuron
{
    public Task SendAsync(Synapse synapse, CancellationToken cancellationToken = default);

    public Task<TResponse> SendAsync<TResponse>(
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Synapse;
}

public interface IDigitalBrain
{
    OwnerId Owner { get; }
    NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron;
}
```

The exact active catalog contains immutable descriptors equivalent to:

```text
Module: stable ID, version, description, configuration keys
Neuron: stable contract ID, default instance convention, description
Accepted/Emitted Synapse: stable ID, major/schema version, JSON schema, description, examples
State: compiled/selected/active
```

Names may follow existing repository conventions, but these semantics and stable identities are required.

## Task 1: Add typed neuron-reference contract tests

**Files:**
- Modify: `src/DigitalBrain.PublishGate.Tests/Contracts/ClientSurface.cs`
- Modify: `src/DigitalBrain.PublishGate.Tests/Client/SendOrdering.cs`
- Create: `src/core/kernel/DigitalBrain.Abstractions/Synapses/RequestSynapse.cs`
- Create: `src/core/kernel/DigitalBrain.Client/NeuronReference.cs`
- Modify: `src/core/kernel/DigitalBrain.Client/IDigitalBrain.cs`
- Modify: `src/core/kernel/DigitalBrain.Client/DigitalBrainClient.cs`
- Create: `src/core/kernel/DigitalBrain.Abstractions/Security/ProtectedPayloadReference.cs`
- Test: `src/DigitalBrain.PublishGate.Tests/DigitalBrain.PublishGate.Tests.csproj`

- [ ] CodeGraph `IDigitalBrain.Get`, `DigitalBrainClient.SendAsync`, `ISessionNeuron.Fire`, journal watch, and all direct callers.
- [ ] Add compile-time public-surface tests proving `Get<IGmail>()`-style references can send one-way synapses and infer a typed response from `RequestSynapse<TResponse>`.
- [ ] Add a real-cluster test proving the response is matched by correlation and cannot be stolen by a concurrent request.
- [ ] Add cancellation tests proving cancellation tears down the watch and does not cancel or corrupt an already committed delivery.
- [ ] Add the shared opaque `ProtectedPayloadReference` value contract with stable identity and expiration metadata but no payload, provider, secret, or storage API.
- [ ] Run:

```powershell
dotnet test src/DigitalBrain.PublishGate.Tests -c Release --filter "ClientSurface|SendOrdering"
```

Expected RED: `RequestSynapse<TResponse>`, `NeuronReference<TNeuron>`, and the new `Get` return shape do not exist.

- [ ] Implement the minimum request/result journal plumbing. Responses must remain synapses and must be journaled.
- [ ] Keep a narrowly named, `[EditorBrowsable(EditorBrowsableState.Never)]` proxy accessor only for method-shaped callers not migrated in this slice. List every caller in the handoff; Slice 8 removes the seam.
- [ ] Re-run the focused tests and then:

```powershell
dotnet test src/DigitalBrain.PublishGate.Tests -c Release
dotnet build src/core/kernel/DigitalBrain.Client/DigitalBrain.Client.csproj -c Release
```

Expected GREEN.

- [ ] Commit: `feat: add typed directed synapse client`

## Task 2: Generate capability descriptors

**Files:**
- Create: `src/core/kernel/DigitalBrain.Abstractions/Capabilities/CapabilityManifest.cs`
- Modify: `src/core/kernel/DigitalBrain/ICompiledModule.cs`
- Modify: `src/core/kernel/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Modify: `src/core/kernel/DigitalBrain.SourceGeneration/DispatchManifestGenerator.Modules.cs`
- Modify: `src/core/kernel/DigitalBrain.SourceGeneration/DispatchManifestGenerator.Neurons.cs`
- Modify: `src/core/kernel/DigitalBrain.SourceGeneration/DispatchManifestGenerator.Wirings.cs`
- Create: `src/core/kernel/DigitalBrain.SourceGeneration/DispatchManifestGenerator.Capabilities.cs`
- Create: `src/core/kernel/DigitalBrain.SourceGeneration.Tests/DigitalBrain.SourceGeneration.Tests.csproj`
- Create: `src/core/kernel/DigitalBrain.SourceGeneration.Tests/CapabilityManifestGeneration.cs`
- Integrator only: add the new test project to `DigitalBrain.slnx`

- [ ] Use Context7 for the current System.Text.Json JSON schema/export API before choosing compile-time versus activation-time schema materialization.
- [ ] Add generator-driver tests for a sample module with one marker neuron, one request synapse, one response synapse, an `IHandle<T>`, and an `IEmit<T>`.
- [ ] Assert deterministic ordering and stable IDs independent of source declaration order.
- [ ] Assert diagnostics for missing aliases/versions/descriptions required by the canonical manifest.
- [ ] Run:

```powershell
dotnet test src/core/kernel/DigitalBrain.SourceGeneration.Tests -c Release
```

Expected RED: no capability manifest is generated.

- [ ] Extend generated `ICompiledModule` implementation with its immutable capability manifest.
- [ ] Reuse existing `IHandle<T>`/`IEmit<T>` analysis; do not create reflection scanning or a second module-discovery mechanism.
- [ ] Ensure schema text is canonical/deterministic and does not persist CLR assembly-qualified names.
- [ ] Re-run the test project and inspect generated output.
- [ ] Build the new test project directly; do not edit `DigitalBrain.slnx` in the parallel Slice 1 worktree.
- [ ] Commit: `feat: generate module capability manifests`

## Task 3: Filter the exact catalog by active Aspire selection

**Files:**
- Create: `src/core/kernel/DigitalBrain/Capabilities/ActiveCapabilityCatalog.cs`
- Modify: `src/core/kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs`
- Modify: `src/core/kernel/DigitalBrain/Hosting/DigitalBrainSiloBuilderExtensions.cs`
- Create: `src/core/testing/DigitalBrain.Testing.Tests/ActiveCapabilityCatalog.cs`
- Modify: `src/core/testing/DigitalBrain.Testing.Tests/DigitalBrain.Testing.Tests.csproj`

- [ ] Add a real test cluster with two compiled modules but only one selected in the module manifest.
- [ ] Assert that only selected module capabilities are active, while unavailable selected modules retain the existing startup failure.
- [ ] Assert exact lookup by stable module/neuron/synapse IDs and version.
- [ ] Assert duplicate IDs or incompatible schemas fail startup with a precise error.
- [ ] Run:

```powershell
dotnet test src/core/testing/DigitalBrain.Testing.Tests -c Release --filter "ActiveCapabilityCatalog"
```

Expected RED.

- [ ] Register one exact active catalog from the selected generated module manifests in `DigitalBrainRuntime.Add`.
- [ ] Keep it authoritative and independent from vector search.
- [ ] Do not add account/provider knowledge.
- [ ] Re-run focused and owning project tests.
- [ ] Commit: `feat: expose selected capability catalog`

## Task 4: Reply semantics and migration proof

**Files:**
- Modify: `src/core/kernel/DigitalBrain/Neuron/Neuron.Messaging.cs`
- Create: `src/core/kernel/DigitalBrain/Neuron/Neuron.Replies.cs`
- Modify: `src/core/kernel/DigitalBrain/Neuron/Neuron.cs`
- Create: `src/core/testing/DigitalBrain.Testing.Tests/DirectedRequestReply.cs`

- [ ] Add tests for `ReplyAsync(response, cancellationToken)` targeting the request caller with the original correlation.
- [ ] Prove request and response are present in incoming/outgoing journals and replaying a duplicate request does not duplicate a proven response.
- [ ] Prove owner isolation and reject replies outside the active delivery context.
- [ ] Run focused tests and capture RED.
- [ ] Implement reply plumbing with ordinary cancellation propagation.
- [ ] Re-run:

```powershell
dotnet test src/core/testing/DigitalBrain.Testing.Tests -c Release --filter "DirectedRequestReply"
dotnet test src/DigitalBrain.PublishGate.Tests -c Release
```

Expected GREEN.

- [ ] CodeGraph all new public symbols and list every remaining compatibility caller.
- [ ] Commit: `feat: journal directed synapse replies`

## Slice Verification

- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] `dotnet test src/core/kernel/DigitalBrain.SourceGeneration.Tests -c Release`
- [ ] `dotnet test src/core/testing/DigitalBrain.Testing.Tests -c Release`
- [ ] `dotnet test src/DigitalBrain.PublishGate.Tests -c Release`
- [ ] Grep public neuron contracts changed in this slice and confirm no operation-specific method was introduced.
- [ ] Run a read-only Grok reviewer against the committed worktree.
- [ ] Return the standard Grok handoff.
