# Slice 4: Reusable Vector Memory and Qdrant Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable MemoryModule whose public `IVectorMemory` neuron supports owner-scoped vector storage/search/removal for system, user, module, behavior, and community uses, while Qdrant remains a replaceable provider.

**Architecture:** Public synapses contain namespace, stable key, text, metadata, and optional protected payload reference—not Qdrant types or raw embedding vectors. The module embeds and stores through an internal provider. Reserved namespaces protect capability/behavior projections. The exact capability catalog remains authoritative; the vector projection is rebuildable.

**Tech Stack:** DigitalBrain modules/neurons/synapses, Microsoft.Extensions.AI embeddings, provider abstraction, in-memory test provider, Qdrant client and Aspire Qdrant integration selected from current docs, xUnit v3.

## Global Constraints

- The product and IAW use Qdrant; do not introduce “Quadrant” APIs.
- `IVectorMemory` must be independently useful to community modules and user-created behaviors.
- Do not expose provider collections, point IDs, filters, distances, embeddings, or client types publicly.
- Do not combine graph memory with vector memory. `IGraphMemory` is future work.
- Owner isolation is implicit in neuron identity and enforced server-side.

---

## Public Contract End State

```csharp
public partial interface IVectorMemory : INeuron;

public sealed record StoreVectorMemory(
    VectorMemoryNamespace Namespace,
    string Key,
    string Text,
    IReadOnlyDictionary<string, string> Metadata,
    ProtectedPayloadReference? Payload) : RequestSynapse<VectorMemoryStored>;

public sealed record SearchVectorMemory(
    VectorMemoryNamespace Namespace,
    string Query,
    int Limit,
    IReadOnlyDictionary<string, string>? Metadata) : RequestSynapse<VectorMemoryMatches>;

public sealed record RemoveVectorMemory(
    VectorMemoryNamespace Namespace,
    string Key) : RequestSynapse<VectorMemoryRemoved>;
```

Exact record names may align with repository vocabulary, but the semantics and provider independence are required.

## Task 1: Add contracts, namespace policy, and in-memory provider

**Files:**
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Contracts/DigitalBrain.Modules.Memory.Contracts.csproj`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Contracts/IVectorMemory.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Contracts/VectorMemoryNamespace.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Contracts/VectorMemoryCommands.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Contracts/VectorMemoryResults.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/DigitalBrain.Modules.Memory.csproj`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/MemoryModule.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/VectorMemoryNeuron.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/IVectorMemoryProvider.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/InMemoryVectorMemoryProvider.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Tests/DigitalBrain.Modules.Memory.Tests.csproj`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Tests/VectorMemoryContract.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Tests/VectorMemoryIsolation.cs`
- Integrator only: add the new projects to `DigitalBrain.slnx`

- [ ] CodeGraph Time/Tasks module conventions and IAW `IawMemoryProvider`/`AgentRegistryGrain`; reuse ideas, not IAW public types.
- [ ] Add one provider-contract suite that can run against any provider.
- [ ] Add RED tests for store/search/remove, deterministic top-k order, metadata filters, owner isolation, cancellation, duplicate key replacement, and missing key removal.
- [ ] Add RED tests proving community/user namespaces cannot write reserved `digitalbrain.capabilities` or `digitalbrain.behaviors`.
- [ ] Implement contracts, MemoryModule, neuron, provider abstraction, and in-memory provider.
- [ ] Build/test the new projects directly; do not edit `DigitalBrain.slnx` in the parallel worktree.
- [ ] The module implementation—not callers—owns embedding generation.
- [ ] Run:

```powershell
dotnet test src/modules/memory/DigitalBrain.Modules.Memory.Tests -c Release
```

Expected GREEN after implementation.

- [ ] Commit: `feat: add reusable vector memory module`

## Task 2: Add Qdrant behind the provider boundary

**Files:**
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Qdrant/DigitalBrain.Modules.Memory.Qdrant.csproj`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Qdrant/QdrantVectorMemoryProvider.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Aspire.Hosting/DigitalBrain.Modules.Memory.Aspire.Hosting.csproj`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Aspire.Hosting/MemoryHostingExtensions.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Tests/QdrantVectorMemoryContract.cs`
- Modify: `Directory.Packages.props`
- Integrator only: add the new projects to `DigitalBrain.slnx`

- [ ] Use Context7 for current Qdrant .NET client APIs.
- [ ] Use Aspire MCP `aspire__search_docs`/`aspire__get_doc` and `aspire__list_integrations` for the current Qdrant resource/client integration; do not copy stale IAW package versions.
- [ ] Run the same provider contract suite against a real Qdrant resource in an integration fixture.
- [ ] Prove collection creation is idempotent, dimensions are validated, cancellation reaches the client, and provider failures do not leak provider DTOs publicly.
- [ ] Add `WithQdrant(...)` configuration on `DigitalBrainModuleBuilder<MemoryModule>`.
- [ ] Keep collection/resource names and connection details internal.
- [ ] Commit: `feat: add qdrant vector memory provider`

## Task 3: Build protected capability and behavior projections

**Files:**
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/Projection/CapabilityProjection.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/Projection/BehaviorProjection.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory/Projection/ProjectionReconciler.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Contracts/ProjectionFacts.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Tests/CapabilityProjection.cs`
- Create: `src/modules/memory/DigitalBrain.Modules.Memory.Tests/BehaviorProjection.cs`

- [ ] Add RED tests that project active exact-catalog entries into the reserved capability namespace.
- [ ] Prove rebuilding is idempotent and removes stale inactive entries.
- [ ] Prove published behavior descriptions/scenarios become searchable and draft/stopped/private artifacts obey visibility policy.
- [ ] Prove vector candidates carry stable exact IDs and cannot override exact schema/version truth.
- [ ] Reconcile via existing Tasks where durability/retry is needed; do not add an Orleans startup-task lifecycle copied from IAW.
- [ ] Keep raw protected payloads, secrets, auth data, and provider results out of projection text/metadata.
- [ ] Commit: `feat: project capabilities into vector memory`

## Task 4: Specify and verify MemoryModule composition

**Files:**
- Integrator only: `os/DigitalBrain.OS.AppHost/AppHost.cs`
- Integrator only: `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`
- Integrator only: `os/DigitalBrain.OS.Host/DigitalBrain.OS.Host.csproj`
- Modify: `os/tests/DigitalBrain.OS.Composition.Tests/CompositionBehaviorShape.cs`
- Modify: `os/tests/DigitalBrain.OS.Composition.Tests/CompositionsFixture.cs`

- [ ] Add a failing composition expectation and return the exact `MemoryModule`/`WithQdrant` AppHost changes for the Wave 2 composition integrator; do not edit shared AppHost/Host files in the Slice 4 worktree.
- [ ] Add a composition test proving selected module metadata and provider resource wiring.
- [ ] Verify the resource only through Aspire MCP on the integrated build.
- [ ] Commit: `feat: compose product vector memory`

## Slice Verification

- [ ] `dotnet test src/modules/memory/DigitalBrain.Modules.Memory.Tests -c Release`
- [ ] `dotnet test os/tests/DigitalBrain.OS.Composition.Tests -c Release`
- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] Aspire MCP shows Qdrant and dependent resources healthy.
- [ ] Use real-module/provider contract tests plus Aspire MCP Qdrant resource health in this slice. Product-flow neuron/journal proof is deferred to Slices 6 and 8, after a consumer exists.
- [ ] Public API review finds no Qdrant symbols and no graph-memory compromise.
- [ ] Return the standard handoff.
