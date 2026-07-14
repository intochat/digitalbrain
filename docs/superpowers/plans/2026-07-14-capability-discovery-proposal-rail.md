# Capability Discovery and Proposal Rail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Chat an authorized hybrid capability catalog it can expose, and create a durable Feature draft proposal when no capability safely matches.

**Architecture:** Runtime-composed capability descriptors become the source of truth. A deterministic resolver filters unavailable descriptors, combines exact, lexical, and embedding similarity, and returns match, ambiguity, or missing without letting the model invent capabilities. The model extracts capability parameters only after the server selects a capability; missing actionable requests create an idempotent draft proposal on the existing owner-scoped `FeatureHubGrain` and project a safe capability/proposal receipt to Flutter.

**Tech Stack:** .NET 11, C# 14, Orleans 10.2, Microsoft.Extensions.AI embeddings, Aspire 13.4, xUnit, Flutter/Dart, GoRouter, existing native conversation surface protocol.

## Architecture reconciliation (2026-07-14, post-consolidation master)

This plan was originally written before all active work was consolidated into master. The following corrections bind every task below; where a task body and this section disagree, this section governs.

| Original plan reference | Current repository reality |
|---|---|
| `src/DigitalBrain.Kernel.Abstractions/*` | Empty stub. All contracts live in `src/DigitalBrain.Kernel.Contracts/` |
| `src/DigitalBrain.Core/{DurableInoContracts,Conversation,ConversationSurfacePayload}.cs` | `src/DigitalBrain.Kernel.Contracts/Core/…` (files exist) |
| `tests/DigitalBrain.Tests/Runtime/*` | Existing files: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/*`. New capability tests: `tests/DigitalBrain.OrleansTests/Capabilities/`. New feature-draft tests: `tests/DigitalBrain.OrleansTests/Features/` |
| `SemanticIntent.cs`, `ConversationModel.cs`, `ConversationModelGrain.cs`, `SemanticProvider`, `SemanticOperation`, `TryExecuteTypedReadAsync`, `TypedReadWorkflowRunnerTests` | Do not exist. There is no intent-selection layer in Chat; `AgentFrameworkWorkflowRunner` sends the prompt straight to a `ChatClientAgent`. Capability resolution is additive, and parameter extraction is a new capability-scoped service |
| `GmailTools.ReadMessages/ReadMailboxOverview/ReadThreads`, `SalesforceTools.DiscoverObjects/SearchRecords/ReadRecords/AggregateRecords/ContinueRecords`, `CrossProviderTools.*` | Do not exist. Real runtime capability IDs: `GoogleCapabilityIds.GmailMessageRead/GmailMailboxRead/GmailSendPropose`, `SalesforceCapabilityIds.RecordRead/RecordUpdatePropose`, `MemoryCapabilityIds.Recall/Remember`, each backed by a registered `ICapabilityHandler`. Effect tool IDs `GmailTools.Send`, `SalesforceTools.UpdateRecord` gate mutations |
| New `FeatureProposal`, `FeatureHubState`, `FeatureHubTransitions`, `IFeatureHubGrain`, `FeatureHubGrain`, `RuntimeStateStorageProviders.GrainState`, `EncryptedPersistentState<FeatureHubState>` | All five type names already exist as the release→approval→grant→install lifecycle rail (`src/DigitalBrain.Kernel.Contracts/FeatureGrainContracts.cs`, `src/DigitalBrain.Kernel/Features/`). Draft proposals extend that existing hub (`FeatureDraftProposal`, `CreateFeatureDraft`, `CreateDraftAsync`) and inherit its `[PersistentState("feature-hub")]` persistence |
| `CapabilityRisk` enum | Reuse existing `CapabilityOperationKind` (Query, InternalWrite, ExternalEffect) |
| Grant snapshot source | `context.Grants` on the authenticated MCP request context (see `RuntimeSessionAuthority`, `UiGrpcService`); external-effect gating already uses `ExternalEffectGrants` (`src/DigitalBrain.Kernel.Contracts/Runtime/InoEffectPlan.cs`) |
| Serialized field appends | Verified against master: `AcceptedCommand` next free is `Id(9)`; `ConversationOperation` next free is `Id(16)` (grants), then `Id(17)` capability, `Id(18)` proposal |

Scope clarification: matching an integration capability in Chat performs deterministic selection plus capability-scoped parameter extraction and projects the receipt; execution of integration reads from Chat remains on the existing FeatureHost/effect rails and is delivered by the next plans. `assistant.answer` matches continue through the existing bounded Chat agent path.

## Global Constraints

- Preserve `Client -> Edge/Auth -> INO operation -> deterministic function or bounded model workflow -> effect gate -> connector adapter`.
- The model may extract typed inputs only after deterministic capability retrieval; it may not create capability identifiers, grants, or availability.
- Capability search must combine structured filtering, exact aliases, lexical overlap, and vector similarity.
- Zero-vector or unavailable embeddings must fall back to deterministic exact and lexical scoring.
- Ambiguous matches must ask for clarification; they must not silently choose.
- Missing actionable work must create an idempotent draft proposal and ask permission to open Studio; it must not install code.
- Conversation history, capability retrieval, and trusted Memory remain separate.
- Every external mutation continues through the existing effect approval rail.
- Keep draft proposals owner-scoped, bounded, and free of credentials or provider payloads; they inherit the existing FeatureHub persistence.
- Do not add a vector database for this slice; the bounded runtime catalog is scored in memory.
- Do not add a new Flutter state-management package.
- Do not modify or regenerate the existing platform plugin registrant files.
- Tracked C#, Dart, Proto, PowerShell, XML, MSBuild, YAML, and JSON source must contain no comments.
- Run the exact root .NET command only: `dotnet test --logger "console;verbosity=minimal"`; never use `--filter`.
- Run all Flutter tests from `app` with `flutter test`.
- After every completed task, run `aspire doctor` and inspect Aspire resources before committing.

## Scope boundary

This plan delivers the first independently releasable Capability OS slice. It does not build Chat-side execution of integration capabilities, editable BDD/source Studio, the persistent navigation shell, operational Home, Connectors, Runs, or governed Memory screens.

---

## File structure

### New backend files

- `src/DigitalBrain.Kernel.Contracts/Runtime/CapabilityDiscovery.cs` — serialized capability descriptor, resolution receipt, draft-proposal reference, search contracts.
- `src/DigitalBrain.Kernel/Capabilities/BuiltInCapabilityCatalog.cs` — explicit descriptors over real capability IDs plus typed bindings.
- `src/DigitalBrain.Kernel/Capabilities/HybridCapabilityResolver.cs` — structured filtering and deterministic exact/lexical/vector ranking.
- `src/DigitalBrain.Kernel/Runtime/CapabilityParameterModel.cs` — capability-scoped bounded parameter extraction.
- `tests/DigitalBrain.OrleansTests/Capabilities/BuiltInCapabilityCatalogTests.cs`
- `tests/DigitalBrain.OrleansTests/Capabilities/HybridCapabilityResolverTests.cs`
- `tests/DigitalBrain.OrleansTests/Capabilities/CapabilityParameterModelTests.cs`
- `tests/DigitalBrain.OrleansTests/Capabilities/CapabilityWorkflowRunnerTests.cs`
- `tests/DigitalBrain.OrleansTests/Features/FeatureDraftTransitionTests.cs`

### Modified backend files

- `src/DigitalBrain.Kernel.Contracts/Core/DurableInoContracts.cs` — grant snapshot on `AcceptedCommand`, grants on `InoWorkflowRequest`, capability/proposal receipts on `InoWorkflowResult`.
- `src/DigitalBrain.Kernel.Contracts/Runtime/ConversationNeuron.cs` — grants and receipts on `ConversationOperation`.
- `src/DigitalBrain.Kernel.Contracts/FeatureGrainContracts.cs` — `FeatureDraftProposal`, `CreateFeatureDraft`, `IFeatureHubGrain.CreateDraftAsync`.
- `src/DigitalBrain.Kernel/Features/FeatureStateModels.cs` — `Drafts` on `FeatureHubState`.
- `src/DigitalBrain.Kernel/Features/FeatureHubTransitions.cs` — `CreateDraft` pure transition.
- `src/DigitalBrain.Kernel/Features/FeatureHubGrain.cs` — `CreateDraftAsync`.
- `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs` — thread grants and receipts through transitions.
- `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs` — pass grants into the workflow request and receipts into completion.
- `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs` — resolve before model use; create draft proposals for missing actionable work.
- `src/DigitalBrain.Mcp/ConversationStateClient.cs` — capture the authenticated grant snapshot when accepting a command.
- `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs` — register catalog, resolver, and parameter model once.
- `src/DigitalBrain.Kernel.Contracts/Core/ConversationSurfacePayload.cs` — serialize the capability chip and proposal action.
- `tests/DigitalBrain.OrleansTests/Legacy/Runtime/ConversationSurfacePayloadTests.cs`
- `tests/DigitalBrain.OrleansTests/Legacy/Runtime/InoReminderHandoffTests.cs`
- `tests/DigitalBrain.OrleansTests/Legacy/Runtime/RuntimeSurfaceFeedTests.cs`
- `tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiGrpcServiceTests.cs`

### Modified Flutter files

- `app/lib/runtime/protocol/surface_protocol.dart` — parse capability and proposal receipts.
- `app/lib/runtime/widgets/ino_conversation_view.dart` — rename INO copy to Chat, render the capability chip, render “Open Studio”.
- `app/lib/router.dart` — add the `/features/proposals/:proposalId` placeholder route.
- `app/lib/runtime/widgets/feature_proposal_placeholder.dart` — new placeholder screen.
- `app/test/runtime/surface_protocol_test.dart`, `app/test/runtime/runtime_shell_test.dart`, `app/test/runtime/grpc_ui_transport_test.dart`

---

### Task 1: Define the capability catalog and typed bindings

**Files:**
- Create: `src/DigitalBrain.Kernel.Contracts/Runtime/CapabilityDiscovery.cs`
- Create: `src/DigitalBrain.Kernel/Capabilities/BuiltInCapabilityCatalog.cs`
- Create: `tests/DigitalBrain.OrleansTests/Capabilities/BuiltInCapabilityCatalogTests.cs`

**Interfaces:**
- Produces: `CapabilityDescriptor`, `CapabilityOrigin`, `CapabilityResolutionKind`, `CapabilityResolutionReceipt`, `FeatureDraftReference`, `CapabilitySearchRequest`, `CapabilityResolution`, `ICapabilityCatalog`, `ICapabilityResolver`, `CapabilityIntentBinding`, `BuiltInCapabilityCatalog`.
- Consumes: existing `GoogleCapabilityIds`, `SalesforceCapabilityIds`, `MemoryCapabilityIds`, `CapabilityOperationKind`, `ExternalEffectGrants`.

- [ ] **Step 1: Write the catalog contract test**

```csharp
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class BuiltInCapabilityCatalogTests
{
    [Fact]
    public void Snapshot_has_unique_stable_ids_and_complete_typed_bindings()
    {
        var catalog = new BuiltInCapabilityCatalog();

        var descriptors = catalog.Snapshot();

        Assert.NotEmpty(descriptors);
        Assert.Equal(descriptors.Count, descriptors.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(descriptors, descriptor =>
        {
            Assert.Matches("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", descriptor.Id);
            Assert.NotEmpty(descriptor.Examples);
            Assert.True(descriptor.Version > 0);
            Assert.True(BuiltInCapabilityCatalog.TryBind(descriptor.Id, out _));
        });
        Assert.Contains(descriptors, x => x.Id == GoogleCapabilityIds.GmailMessageRead);
        Assert.Contains(descriptors, x => x.Id == SalesforceCapabilityIds.RecordRead);
        Assert.Contains(descriptors, x => x.Id == "assistant.answer");
    }
}
```

If the integration contracts projects are not referenced by `DigitalBrain.OrleansTests`, add the project references; do not restate the ID strings.

- [ ] **Step 2: Run the root suite and verify the new test fails**

Run from the repository root in a background job: `dotnet test --logger "console;verbosity=minimal"`.

Expected: FAIL because `BuiltInCapabilityCatalog` and discovery contracts do not exist.

- [ ] **Step 3: Add the public capability discovery contracts**

Create `src/DigitalBrain.Kernel.Contracts/Runtime/CapabilityDiscovery.cs` in namespace `DigitalBrain.Kernel.Capabilities` with these exact public shapes:

```csharp
public enum CapabilityOrigin { Platform, Integration, Feature }
public enum CapabilityResolutionKind { Match, Ambiguous, Missing }

[GenerateSerializer, Alias("digitalbrain.capability.descriptor.v1")]
public sealed record CapabilityDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] int Version,
    [property: Id(2)] string Name,
    [property: Id(3)] string Description,
    [property: Id(4)] string[] Examples,
    [property: Id(5)] string[] RequiredGrants,
    [property: Id(6)] string[] RequiredConnections,
    [property: Id(7)] CapabilityOrigin Origin,
    [property: Id(8)] CapabilityOperationKind Kind,
    [property: Id(9)] bool Available);

[GenerateSerializer, Alias("digitalbrain.capability.resolution-receipt.v1")]
public sealed record CapabilityResolutionReceipt(
    [property: Id(0)] CapabilityResolutionKind Kind,
    [property: Id(1)] string? CapabilityId,
    [property: Id(2)] string? CapabilityName,
    [property: Id(3)] string[] CandidateIds,
    [property: Id(4)] double Confidence);

[GenerateSerializer, Alias("digitalbrain.feature.draft-reference.v1")]
public sealed record FeatureDraftReference(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string Label,
    [property: Id(2)] string Route);

public sealed record CapabilitySearchRequest(
    string Prompt,
    IReadOnlySet<string> Grants,
    IReadOnlySet<string> Connections,
    int MaximumMatches = 3);

public sealed record CapabilityResolution(
    CapabilityResolutionReceipt Receipt,
    CapabilityDescriptor? Selected,
    IReadOnlyList<CapabilityDescriptor> Candidates);

public interface ICapabilityCatalog
{
    IReadOnlyList<CapabilityDescriptor> Snapshot();
}

public interface ICapabilityResolver
{
    Task<CapabilityResolution> ResolveAsync(
        CapabilitySearchRequest request,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add explicit built-in descriptors and bindings**

Create `src/DigitalBrain.Kernel/Capabilities/BuiltInCapabilityCatalog.cs`. Declare descriptors for `assistant.answer`, `GoogleCapabilityIds.GmailMessageRead`, `GoogleCapabilityIds.GmailMailboxRead`, `GoogleCapabilityIds.GmailSendPropose`, `SalesforceCapabilityIds.RecordRead`, `SalesforceCapabilityIds.RecordUpdatePropose`, `MemoryCapabilityIds.Recall`, and `MemoryCapabilityIds.Remember`. Each descriptor must have human examples and exact connection/grant requirements. The binding carries the dispatch identity, not a semantic enum:

```csharp
public sealed record CapabilityIntentBinding(string CapabilityId, int CapabilityVersion, CapabilityOperationKind Kind);
```

`BuiltInCapabilityCatalog : ICapabilityCatalog` keeps a `Bindings` dictionary keyed by capability ID and a static `TryBind(string capabilityId, out CapabilityIntentBinding binding)`. Descriptors are produced by an internal factory using a switch expression with one complete descriptor per binding key, throwing for an unrecognized key. Do not derive IDs from CLR member names. Connection requirements: `google` for Gmail descriptors, `salesforce` for Salesforce descriptors, none for `assistant.answer` and memory descriptors. Grant requirements: the exact `ExternalEffectGrants` grant constant for the two effect-proposal descriptors (`GmailSendPropose`, `RecordUpdatePropose`); none otherwise. `assistant.answer` has `CapabilityOrigin.Platform` and `CapabilityOperationKind.Query`; memory descriptors are Platform; integration descriptors are Integration. All descriptors are `Available = true` and `Version = 1`.

- [ ] **Step 5: Run the root suite and verify it passes**

Expected: PASS with zero failed tests.

- [ ] **Step 6: Verify Aspire and commit**

Run `aspire doctor`, inspect resources, then commit exactly:

```powershell
git commit -m "feat: define capability catalog"
```

---

### Task 2: Implement deterministic hybrid capability resolution

**Files:**
- Create: `src/DigitalBrain.Kernel/Capabilities/HybridCapabilityResolver.cs`
- Create: `tests/DigitalBrain.OrleansTests/Capabilities/HybridCapabilityResolverTests.cs`
- Modify: `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs`

**Interfaces:**
- Consumes: `ICapabilityCatalog`, `IEmbeddingGenerator<string, Embedding<float>>`, `CapabilitySearchRequest`.
- Produces: `HybridCapabilityResolver.ResolveAsync` implementing `ICapabilityResolver`.

- [ ] **Step 1: Write ranking, fallback, ambiguity, and filtering tests**

Create `HybridCapabilityResolverTests.cs` with four facts using a deterministic fake embedding generator:

```csharp
[Fact]
public async Task ResolveAsync_selects_salesforce_read_from_semantic_similarity()
{
    var resolver = Resolver(new Dictionary<string, float[]>
    {
        ["Find Acme in our CRM"] = [1, 0],
        [Document(SalesforceCapabilityIds.RecordRead)] = [1, 0],
        [Document(GoogleCapabilityIds.GmailMessageRead)] = [0, 1]
    });

    var result = await resolver.ResolveAsync(Request("Find Acme in our CRM", connections: ["salesforce"]));

    Assert.Equal(CapabilityResolutionKind.Match, result.Receipt.Kind);
    Assert.Equal(SalesforceCapabilityIds.RecordRead, result.Receipt.CapabilityId);
}

[Fact]
public async Task ResolveAsync_falls_back_to_lexical_scoring_for_zero_vectors()
{
    var result = await ResolverWithZeroVectors().ResolveAsync(Request("read gmail messages", connections: ["google"]));

    Assert.Equal(GoogleCapabilityIds.GmailMessageRead, result.Receipt.CapabilityId);
}

[Fact]
public async Task ResolveAsync_returns_ambiguous_when_top_scores_are_too_close()
{
    var result = await ResolverWithEqualVectors().ResolveAsync(Request("show customer records", connections: ["google", "salesforce"]));

    Assert.Equal(CapabilityResolutionKind.Ambiguous, result.Receipt.Kind);
    Assert.True(result.Receipt.CandidateIds.Length >= 2);
}

[Fact]
public async Task ResolveAsync_filters_missing_grants_before_scoring()
{
    var result = await ResolverWithExactVectors().ResolveAsync(Request("send an email", connections: ["google"]));

    Assert.DoesNotContain(GoogleCapabilityIds.GmailSendPropose, result.Receipt.CandidateIds);
    Assert.NotEqual(GoogleCapabilityIds.GmailSendPropose, result.Receipt.CapabilityId);
}
```

The shared `Request` helper must default grants and connections to empty `HashSet<string>(StringComparer.Ordinal)`. The fake generator must return configured vectors by exact input and `[0, 0]` otherwise. `Document(id)` returns the exact search document the resolver builds for that descriptor so the semantic test configures the true embedding input.

- [ ] **Step 2: Run the root suite and verify failure**

Expected: FAIL because `HybridCapabilityResolver` does not exist.

- [ ] **Step 3: Implement the resolver**

Create `HybridCapabilityResolver.cs` with these constants and decision rules:

```csharp
public sealed class HybridCapabilityResolver(
    ICapabilityCatalog catalog,
    IEmbeddingGenerator<string, Embedding<float>> embedder) : ICapabilityResolver
{
    internal const double MatchThreshold = 0.68;
    internal const double AmbiguityMargin = 0.06;
    internal const int MaximumPromptLength = 4096;

    public async Task<CapabilityResolution> ResolveAsync(
        CapabilitySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        if (request.Prompt.Length > MaximumPromptLength || request.MaximumMatches is < 1 or > 5)
            throw new ArgumentException("Capability search bounds are invalid.", nameof(request));

        var candidates = catalog.Snapshot()
            .Where(x => x.Available)
            .Where(x => x.RequiredGrants.All(request.Grants.Contains))
            .Where(x => x.RequiredConnections.All(request.Connections.Contains))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return Missing();

        var query = Normalize(request.Prompt);
        var documents = candidates.Select(SearchDocument).ToArray();
        var generated = await embedder.GenerateAsync([request.Prompt, .. documents], cancellationToken: cancellationToken);
        var queryVector = generated[0].Vector.Span;
        var vectorEnabled = queryVector.IndexOfAnyExcept(0f) >= 0;
        var ranked = candidates.Select((descriptor, index) => new RankedCapability(
                descriptor,
                Exact(query, descriptor),
                Lexical(query, SearchDocument(descriptor)),
                vectorEnabled ? Cosine(queryVector, generated[index + 1].Vector.Span) : 0))
            .Select(x => x with { Score = vectorEnabled
                ? Math.Max(0.65 * x.Exact + 0.35 * x.Lexical, 0.70 * x.Vector + 0.30 * x.Lexical)
                : 0.65 * x.Exact + 0.35 * x.Lexical })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Descriptor.Id, StringComparer.Ordinal)
            .Take(request.MaximumMatches)
            .ToArray();

        var first = ranked[0];
        if (first.Score < MatchThreshold) return Missing(ranked);
        if (ranked.Length > 1 && first.Score - ranked[1].Score < AmbiguityMargin)
            return Ambiguous(ranked);
        return Match(first, ranked);
    }
}
```

Implement `Normalize`, `SearchDocument`, `Exact`, `Lexical`, and `Cosine` as pure bounded helpers in the same file. Exact scoring returns `1` for a normalized capability ID, name, or complete example match, `0.8` when the prompt contains the normalized name, and `0` otherwise. Lexical scoring is Jaccard similarity over distinct lowercase letter/digit tokens. Cosine returns `0` for zero norms and clamps to `0..1`. `Missing`, `Ambiguous`, and `Match` must populate only safe descriptor metadata in the receipt.

- [ ] **Step 4: Register the catalog and resolver**

In `DigitalBrainOrleansExtensions.cs`, add exactly one singleton registration for each service in the existing RuntimeHost service composition:

```csharp
services.TryAddSingleton<ICapabilityCatalog, BuiltInCapabilityCatalog>();
services.TryAddSingleton<ICapabilityResolver, HybridCapabilityResolver>();
```

Verify `IEmbeddingGenerator<string, Embedding<float>>` is already registered in the same composition (existing embedding registration tests cover it); if the runner host lacks it, resolve lazily so hosts without embeddings still start.

- [ ] **Step 5: Run the root suite and verify it passes**

Expected: PASS with zero failed tests, including all four resolver cases.

- [ ] **Step 6: Verify Aspire and commit**

```powershell
git commit -m "feat: resolve capabilities with hybrid search"
```

---

### Task 3: Constrain model extraction to the selected capability

**Files:**
- Create: `src/DigitalBrain.Kernel/Runtime/CapabilityParameterModel.cs`
- Create: `tests/DigitalBrain.OrleansTests/Capabilities/CapabilityParameterModelTests.cs`

**Interfaces:**
- Consumes: `BuiltInCapabilityCatalog.TryBind`, `IChatClient` structured output, `RetainedInoCapabilityPayload`.
- Produces: `ICapabilityParameterModel.ExtractAsync` returning a payload whose tool ID must equal the server-selected capability.

- [ ] **Step 1: Add a failing model-boundary test**

```csharp
[Fact]
public async Task ExtractAsync_rejects_a_model_selected_capability_change()
{
    var chat = RecordingChatClientReturning(toolId: SalesforceCapabilityIds.RecordRead, argumentsJson: "{\"query\":\"Acme\"}");
    var model = new CapabilityParameterModel(chat);

    await Assert.ThrowsAsync<InvalidOperationException>(() => model.ExtractAsync(new CapabilityParameterRequest(
        GoogleCapabilityIds.GmailMessageRead,
        "list recent mail")));
}

[Fact]
public async Task ExtractAsync_rejects_an_unknown_capability()
{
    var model = new CapabilityParameterModel(RecordingChatClientReturning(GoogleCapabilityIds.GmailMessageRead, "{}"));

    await Assert.ThrowsAsync<ArgumentException>(() => model.ExtractAsync(new CapabilityParameterRequest(
        "not.a.capability",
        "list recent mail")));
}
```

- [ ] **Step 2: Run the root suite and verify failure**

Expected: FAIL because `CapabilityParameterModel` does not exist.

- [ ] **Step 3: Implement capability-scoped extraction**

`CapabilityParameterRequest(string CapabilityId, string Prompt)` with prompt bounded to 4096 characters. `ExtractAsync` must first call `BuiltInCapabilityCatalog.TryBind(request.CapabilityId, out var binding)` and throw `ArgumentException` for an unknown ID. The extraction guidance states the capability is a fixed server decision. After structured output, reject any tool ID that differs:

```csharp
if (!string.Equals(extracted.ToolId, request.CapabilityId, StringComparison.Ordinal))
    throw new InvalidOperationException("The extraction model changed the selected capability.");
```

Return a `RetainedInoCapabilityPayload` (existing bounded type). Register `ICapabilityParameterModel` as a singleton beside the resolver.

- [ ] **Step 4: Run the root suite and verify it passes**

Expected: PASS with zero failed tests.

- [ ] **Step 5: Verify Aspire and commit**

```powershell
git commit -m "refactor: bind intent extraction to capabilities"
```

---

### Task 4: Route Chat through the capability resolver

**Files:**
- Modify: `src/DigitalBrain.Kernel.Contracts/Core/DurableInoContracts.cs`
- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Mcp/ConversationStateClient.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs`
- Create: `tests/DigitalBrain.OrleansTests/Capabilities/CapabilityWorkflowRunnerTests.cs`

**Interfaces:**
- Consumes: `ICapabilityResolver`, `ICapabilityParameterModel`, `CapabilitySearchRequest`.
- Produces: `InoWorkflowResult.Capability` for match, ambiguity, and missing outcomes.

- [ ] **Step 1: Write failing runner tests**

Create three tests:

```csharp
[Fact]
public async Task ExecuteAsync_resolves_before_calling_the_parameter_model()
{
    var resolver = new RecordingCapabilityResolver(Match(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"));
    var runner = Runner(resolver);

    var result = await runner.ExecuteAsync(Request("list my latest messages"));

    Assert.Equal(1, resolver.CallCount);
    Assert.Equal(GoogleCapabilityIds.GmailMessageRead, result.Capability?.CapabilityId);
    Assert.Equal(GoogleCapabilityIds.GmailMessageRead, ParameterModel.LastRequest?.CapabilityId);
}

[Fact]
public async Task ExecuteAsync_returns_clarification_for_ambiguous_capabilities()
{
    var runner = Runner(new RecordingCapabilityResolver(Ambiguous(GoogleCapabilityIds.GmailMessageRead, GoogleCapabilityIds.GmailMailboxRead)));

    var result = await runner.ExecuteAsync(Request("show my mail"));

    Assert.Equal(CapabilityResolutionKind.Ambiguous, result.Capability?.Kind);
    Assert.Contains("choose", result.Text, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(0, ParameterModel.CallCount);
}

[Fact]
public async Task ExecuteAsync_does_not_call_the_general_agent_for_missing_actionable_work()
{
    var runner = Runner(new RecordingCapabilityResolver(Missing()));

    var result = await runner.ExecuteAsync(Request("Research Acme and create a text file"));

    Assert.Equal(CapabilityResolutionKind.Missing, result.Capability?.Kind);
    Assert.Equal(0, Chat.CallCount);
}
```

- [ ] **Step 2: Run the root suite and verify failure**

Expected: FAIL because the workflow result has no capability receipt and the runner does not resolve capabilities.

- [ ] **Step 3: Extend the workflow result safely**

Append optional fields to `InoWorkflowResult`:

```csharp
public sealed record InoWorkflowResult(
    string Text,
    WorkflowReference Workflow,
    InoToolRequest? ToolRequest = null,
    InoAuthorizationRequest? AuthorizationRequest = null,
    CapabilityResolutionReceipt? Capability = null,
    FeatureDraftReference? Proposal = null);
```

Do not add embeddings, prompts, provider payloads, or candidate descriptions to this durable boundary.

- [ ] **Step 4: Capture the authenticated grant snapshot**

Append `[property: Id(9)] string[] Grants` to `AcceptedCommand` and `[property: Id(16)] string[] Grants` to `ConversationOperation` without changing existing IDs (verified free against master). Append `IReadOnlyList<string>? Grants = null` as the final parameter of `InoWorkflowRequest`.

`ConversationStateClient.BeginAsync` must sort and copy `context.Grants` into `AcceptedCommand`. `ConversationTransitions.BeginOperation` must copy that array into `ConversationOperation`. `InoOperationWorkerGrain` must pass the claimed grants to `InoWorkflowRequest`. Validation must reject null entries, control characters, duplicates, more than 64 grants, and grant strings longer than 128 characters. This snapshot controls discovery only; the existing effect gate remains authoritative for execution and revocation.

- [ ] **Step 5: Resolve before the general agent**

In `AgentFrameworkWorkflowRunner.ExecuteAsync`, resolve once with the bounded prompt before creating the agent. Add this current-composition constant beside the runner bounds; the Feature runtime plan replaces it with owner-scoped installed Feature contributions:

```csharp
private static readonly IReadOnlySet<string> ComposedIntegrationIds =
    new HashSet<string>(["google", "salesforce"], StringComparer.Ordinal);
```

Control flow: build `CapabilitySearchRequest(request.Prompt, grants, ComposedIntegrationIds, 3)` from the server-known grants. Ambiguous returns the clarification result with the receipt and calls no model. Missing calls `CreateMissingCapabilityResultAsync` (Task 5 wires the grain; in this task it returns the missing receipt with bounded text). A match on `assistant.answer` continues through the existing `ChatClientAgent` path with the receipt attached. A match on any other capability calls `ICapabilityParameterModel.ExtractAsync` with the selected capability ID, then returns a bounded acknowledgment naming the capability with the receipt attached; Chat-side execution of integration capabilities arrives in the next plan.

- [ ] **Step 6: Run the root suite and verify it passes**

Expected: PASS with zero failed tests and no behavior regression.

- [ ] **Step 7: Verify Aspire and commit**

```powershell
git commit -m "feat: resolve chat requests through capabilities"
```

---

### Task 5: Persist idempotent missing-capability draft proposals

**Files:**
- Modify: `src/DigitalBrain.Kernel.Contracts/FeatureGrainContracts.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureStateModels.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureHubTransitions.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureHubGrain.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs`
- Create: `tests/DigitalBrain.OrleansTests/Features/FeatureDraftTransitionTests.cs`

**Interfaces:**
- Produces: `FeatureDraftProposal`, `CreateFeatureDraft`, `FeatureHubTransitions.CreateDraft`, `IFeatureHubGrain.CreateDraftAsync`.
- Consumes: owner scope, operation ID, original prompt, `TimeProvider`, existing `FeatureHubState` persistence.

- [ ] **Step 1: Write transition tests**

```csharp
[Fact]
public void CreateDraft_is_idempotent_for_the_same_operation()
{
    var request = new CreateFeatureDraft("operation-1", "Research Acme and create a text file", Now);

    var first = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, request);
    var second = FeatureHubTransitions.CreateDraft(first.State, OwnerScope, request);

    Assert.Equal(first.Draft, second.Draft);
    Assert.Same(first.State, second.State);
}

[Fact]
public void CreateDraft_rejects_unbounded_or_control_character_prompts()
{
    Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateDraft(
        EmptyState,
        OwnerScope,
        new CreateFeatureDraft("operation-1", new string('x', 4097), Now)));
    Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateDraft(
        EmptyState,
        OwnerScope,
        new CreateFeatureDraft("operation-2", "unsafe prompt", Now)));
}
```

- [ ] **Step 2: Run the root suite and verify failure**

Expected: FAIL because draft contracts and transitions do not exist.

- [ ] **Step 3: Add draft contracts and the pure transition**

Extend `FeatureGrainContracts.cs`:

```csharp
[GenerateSerializer, Alias("digitalbrain.feature.draft-proposal.v1")]
public sealed record FeatureDraftProposal(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string Goal,
    [property: Id(3)] string Status,
    [property: Id(4)] DateTimeOffset CreatedAt);

[GenerateSerializer, Alias("digitalbrain.feature.create-draft.v1")]
public sealed record CreateFeatureDraft(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string Goal,
    [property: Id(2)] DateTimeOffset RequestedAt);
```

Add `[Alias("create-draft")] Task<FeatureDraftProposal> CreateDraftAsync(CreateFeatureDraft request);` to the existing `IFeatureHubGrain`. Append a `FeatureDraftProposal[] Drafts` field to the existing internal `FeatureHubState` using the next free serializer ID with `= []` compatibility default.

`FeatureHubTransitions.CreateDraft` caps drafts at 100 per owner, goals at 4096 characters, and returns the existing state instance unchanged when the same operation ID repeats with the same goal digest. Derive `ProposalId` as `proposal-` plus the first 32 lowercase hex characters of SHA-256 over `ownerScope + "\0" + operationId`. New drafts have `Status = "draft"` and increment the hub revision. Store the bounded original goal; do not generate source, permissions, or triggers in this slice.

- [ ] **Step 4: Implement grain surface**

`FeatureHubGrain.CreateDraftAsync` applies the transition, writes state through the existing `[PersistentState("feature-hub")]` persistence, and returns the draft.

- [ ] **Step 5: Create drafts only for missing actionable work**

In `AgentFrameworkWorkflowRunner.CreateMissingCapabilityResultAsync`, classify a missing request as ordinary conversation only when the prompt is a greeting, thanks, help request, or capability question covered by `assistant.answer`; those continue through the existing bounded Chat path. For any other missing request, call the owner-scoped `IFeatureHubGrain` idempotently (keyed by owner via the existing `IFeatureGrainResolver`) and return:

```csharp
return new InoWorkflowResult(
    "I don’t have a trusted capability for that yet. I created a Feature draft. Open Studio to define and verify its behavior?",
    workflow,
    Capability: receipt,
    Proposal: new FeatureDraftReference(
        draft.ProposalId,
        "Open Studio",
        "/features/proposals/" + draft.ProposalId));
```

Do not call a model to decide whether code should be installed.

- [ ] **Step 6: Run the root suite and verify it passes**

Expected: PASS with zero failed tests.

- [ ] **Step 7: Verify Aspire and commit**

```powershell
git commit -m "feat: persist feature proposals for missing capabilities"
```

---

### Task 6: Persist and project safe capability receipts

**Files:**
- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Kernel.Contracts/Core/ConversationSurfacePayload.cs`
- Modify: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/ConversationSurfacePayloadTests.cs`
- Modify: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/InoReminderHandoffTests.cs`

**Interfaces:**
- Consumes: `InoWorkflowResult.Capability` and `.Proposal`.
- Produces: optional `capability` and `proposal` objects in the native `inoConversation` payload.

- [ ] **Step 1: Add failing projection and recovery tests**

Add a payload test asserting exact safe JSON:

```csharp
Assert.Equal("salesforce.record.read.v1", operation.GetProperty("capability").GetProperty("id").GetString());
Assert.Equal("Read Salesforce records", operation.GetProperty("capability").GetProperty("name").GetString());
Assert.Equal("match", operation.GetProperty("capability").GetProperty("kind").GetString());
Assert.Equal("proposal-0123456789abcdef0123456789abcdef", operation.GetProperty("proposal").GetProperty("id").GetString());
Assert.Equal("/features/proposals/proposal-0123456789abcdef0123456789abcdef", operation.GetProperty("proposal").GetProperty("route").GetString());
Assert.DoesNotContain("prompt", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
```

Add a reminder handoff test that persists, deactivates, and reloads an operation carrying both receipts, then asserts equality.

- [ ] **Step 2: Run the root suite and verify failure**

Expected: FAIL because operation state and surface payload do not retain the receipts.

- [ ] **Step 3: Append serialized operation fields**

Append these optional fields to `ConversationOperation` after the grant snapshot added in Task 4:

```csharp
[property: Id(17)] CapabilityResolutionReceipt? Capability = null,
[property: Id(18)] FeatureDraftReference? Proposal = null
```

Thread them through the assistant-completion path (`ConversationTransitions.CompleteWithAssistant` and the worker completion call). Existing callers pass null by default. Add validation that receipt IDs, names, labels, routes, confidence, and candidate counts are bounded and that proposal routes begin with `/features/proposals/proposal-`.

- [ ] **Step 4: Project only safe fields**

In the conversation surface payload builder, emit:

```csharp
operation["capability"] = new Dictionary<string, object?>
{
    ["kind"] = current.Capability.Kind.ToString().ToLowerInvariant(),
    ["id"] = current.Capability.CapabilityId,
    ["name"] = current.Capability.CapabilityName,
    ["confidence"] = current.Capability.Confidence
};
operation["proposal"] = new Dictionary<string, object?>
{
    ["id"] = current.Proposal.ProposalId,
    ["label"] = current.Proposal.Label,
    ["route"] = current.Proposal.Route
};
```

Emit each block only when its source is non-null. Do not emit the original prompt, examples, embeddings, grants, or connection identifiers.

- [ ] **Step 5: Run the root suite and verify it passes**

Expected: PASS with zero failed tests.

- [ ] **Step 6: Verify Aspire and commit**

```powershell
git commit -m "feat: project capability receipts to chat"
```

---

### Task 7: Render capability and proposal UX in Flutter Chat

**Files:**
- Modify: `app/lib/runtime/protocol/surface_protocol.dart`
- Modify: `app/lib/runtime/widgets/ino_conversation_view.dart`
- Modify: `app/lib/router.dart`
- Create: `app/lib/runtime/widgets/feature_proposal_placeholder.dart`
- Modify: `app/test/runtime/surface_protocol_test.dart`
- Modify: `app/test/runtime/runtime_shell_test.dart`

**Interfaces:**
- Consumes: native operation `capability` and `proposal` JSON objects.
- Produces: `InoCapabilityReceipt`, `InoFeatureProposalReference`, a subtle capability chip, and a safe internal Studio route action.

- [ ] **Step 1: Add failing protocol tests**

```dart
test('parses bounded capability and proposal receipts', () {
  final payload = InoConversationSurfacePayload.fromJson(fixtureWith(
    capability: {
      'kind': 'match',
      'id': 'salesforce.record.read.v1',
      'name': 'Read Salesforce records',
      'confidence': 0.91,
    },
    proposal: {
      'id': 'proposal-0123456789abcdef0123456789abcdef',
      'label': 'Open Studio',
      'route': '/features/proposals/proposal-0123456789abcdef0123456789abcdef',
    },
  ));

  expect(payload.operation!.capability!.id, 'salesforce.record.read.v1');
  expect(payload.operation!.proposal!.label, 'Open Studio');
});

test('rejects external or malformed proposal routes', () {
  expect(
    () => InoConversationSurfacePayload.fromJson(fixtureWith(
      proposal: {
        'id': 'proposal-0123456789abcdef0123456789abcdef',
        'label': 'Open Studio',
        'route': 'https://example.com',
      },
    )),
    throwsFormatException,
  );
});
```

- [ ] **Step 2: Run Flutter tests and verify failure**

Run `flutter test` from `app`. Expected: FAIL because receipt models and parsing do not exist.

- [ ] **Step 3: Add strict protocol models**

In `surface_protocol.dart`, add immutable `InoCapabilityReceipt` and `InoFeatureProposalReference` classes with strict `fromWire` factories. Bound IDs to 128 characters, names/labels to 80, candidates to 5, confidence to `0..1`, and routes to the exact internal proposal prefix. Append optional fields to `InoConversationOperation` without changing existing required fields.

- [ ] **Step 4: Render product language and actions**

In `ino_conversation_view.dart`:

- change the semantic label from `INO conversation` to `Chat conversation`;
- change `Ask INO` to `Chat`;
- render a small capability chip above the operation status when a receipt has an ID and name;
- show at most the human name by default;
- render an `Open Studio` button only for a validated proposal reference;
- call `context.go(proposal.route)` for that internal route;
- keep authorization URLs on the existing validated external action path.

Use these stable keys:

```dart
const Key chatCapabilityChipKey = Key('chat-capability-chip');
const Key chatOpenStudioButtonKey = Key('chat-open-studio-button');
```

- [ ] **Step 5: Add the safe placeholder route**

Create `feature_proposal_placeholder.dart` as a stateless scaffold showing the proposal ID, “Feature Studio”, “Draft created from Chat”, and a back-to-Chat button. Add this route:

```dart
GoRoute(
  path: '/features/proposals/:proposalId',
  name: 'feature-proposal',
  builder: (context, state) => FeatureProposalPlaceholder(
    proposalId: state.pathParameters['proposalId']!,
  ),
),
```

The placeholder must not imply verification or installation. The next Feature authoring plan replaces it.

- [ ] **Step 6: Add widget tests**

Add tests that assert the capability chip, `Open Studio` button, internal navigation, Chat copy, and absence of the button when proposal metadata is missing.

- [ ] **Step 7: Run Flutter tests and verify they pass**

Run `flutter test` from `app`. Expected: PASS with zero failed tests.

- [ ] **Step 8: Verify Aspire and commit**

```powershell
git commit -m "feat: show capabilities and feature proposals in chat"
```

---

### Task 8: Prove the vertical slice end to end

**Files:**
- Modify: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/RuntimeSurfaceFeedTests.cs`
- Modify: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiGrpcServiceTests.cs`
- Modify: `app/test/runtime/grpc_ui_transport_test.dart`

**Interfaces:**
- Consumes: the completed capability resolution, draft persistence, feed projection, gRPC transport, and Flutter parsing path.
- Produces: one backend acceptance test and one Flutter transport acceptance test for the Company Research request.

- [ ] **Step 1: Add a backend acceptance test**

The test must submit `Research Acme Corporation and create a text file with the findings.`, wait for the operation result, and assert:

```csharp
Assert.Equal(CapabilityResolutionKind.Missing, operation.Capability?.Kind);
Assert.NotNull(operation.Proposal);
Assert.StartsWith("proposal-", operation.Proposal.ProposalId, StringComparison.Ordinal);
Assert.Equal("Open Studio", operation.Proposal.Label);
Assert.Contains("I don’t have a trusted capability", assistantTurn.Text, StringComparison.Ordinal);
Assert.Equal(0, recordingGeneralChatClient.CallCount);
```

Repeat delivery with the same operation ID and assert exactly one draft in the hub state.

- [ ] **Step 2: Add a Flutter transport acceptance test**

Feed the exact native surface JSON produced by the backend fixture through `GrpcUiTransport`, then assert that `RuntimeController.latestSurface` parses one missing capability receipt and one proposal reference with the safe internal route.

- [ ] **Step 3: Run all verification suites**

Run the root .NET suite in a background job, `flutter test` from `app`, `dart run tool/check_ui_imports.dart` from `app`, and `git diff --check`.

Expected: every command exits `0`; .NET and Flutter report zero failed tests; the import boundary reports `Boundary check: OK`; `git diff --check` prints nothing.

- [ ] **Step 4: Validate the live Aspire application**

Run `aspire doctor`, confirm RuntimeHost, MCP/UI Edge, embed model, storage, and Flutter resources are healthy, restart only the affected RuntimeHost/MCP/Flutter resources, and inspect console logs and traces for one matching and one missing request. Confirm:

- matching Gmail or Salesforce requests show the selected capability ID in trace attributes;
- the Company Research request emits one missing resolution and one proposal ID;
- no prompt, embedding vector, grant, credential, or provider payload appears in logs;
- Flutter opens the internal proposal placeholder and returns to Chat.

If credentials or infrastructure are unavailable, document exactly what could and could not be validated; never fabricate live results.

- [ ] **Step 5: Commit the acceptance proof**

```powershell
git commit -m "test: prove capability proposal flow end to end"
```

## Completion gate

This plan is complete only when all eight task commits exist, the exact root .NET suite and full Flutter suite pass, Aspire is healthy, and a live Company Research request creates one durable draft proposal without calling the general Chat model or installing code.

The next plan begins from that proposal ID and replaces the Flutter placeholder with the living BDD and single-file C# Studio workflow.
