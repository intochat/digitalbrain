# Capability Discovery and Proposal Rail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hardcoded provider-first intent selection with an authorized hybrid capability catalog that Chat can expose, and create a durable Feature proposal when no capability safely matches.

**Architecture:** Runtime-composed capability descriptors become the source of truth. A deterministic resolver filters unavailable descriptors, combines exact, lexical, and embedding similarity, and returns match, ambiguity, or missing without letting the model invent capabilities. The existing conversation/model workflow extracts parameters only after the server selects a capability; missing actionable requests create an idempotent proposal in the owner-scoped `FeatureHubGrain` and project a safe capability/proposal receipt to Flutter.

**Tech Stack:** .NET 11, C# 14, Orleans 10.2, Microsoft.Extensions.AI embeddings, Aspire 13.4, xUnit, Flutter/Dart, GoRouter, existing native conversation surface protocol.

## Global Constraints

- Preserve `Client -> Edge/Auth -> INO operation -> deterministic function or bounded model workflow -> effect gate -> connector adapter`.
- The model may extract typed inputs only after deterministic capability retrieval; it may not create capability identifiers, grants, or availability.
- Capability search must combine structured filtering, exact aliases, lexical overlap, and vector similarity.
- Zero-vector or unavailable embeddings must fall back to deterministic exact and lexical scoring.
- Ambiguous matches must ask for clarification; they must not silently choose.
- Missing actionable work must create an idempotent proposal and ask permission to open Studio; it must not install code.
- Conversation history, capability retrieval, and trusted Memory remain separate.
- Every external mutation continues through the existing effect approval rail.
- Keep Feature proposals owner-scoped, encrypted at rest, bounded, and free of credentials or provider payloads.
- Do not add a vector database for this slice; the bounded runtime catalog is scored in memory.
- Do not add a new Flutter state-management package.
- Do not modify or regenerate the existing platform plugin registrant files.
- Tracked C#, Dart, Proto, PowerShell, XML, MSBuild, YAML, and JSON source must contain no comments.
- Run the exact root .NET command only: `dotnet test --logger "console;verbosity=minimal"`; never use `--filter`.
- Run all Flutter tests from `app` with `flutter test`.
- After every completed task, run `aspire doctor` and inspect Aspire resources before committing.

## Scope boundary

This plan delivers the first independently releasable Capability OS slice. It does not build FeatureBuilder, FeatureHost, editable BDD/source Studio, the persistent navigation shell, operational Home, Connectors, Runs, or governed Memory screens.

After this plan is green, write these independent execution plans in order:

1. `2026-07-14-feature-authoring-company-research.md` — living BDD, one C# file, verification, release, installation, and the Company Research Feature.
2. `2026-07-14-flutter-capability-shell.md` — persistent rail, six routes, contextual global command, and full Chat expansion.
3. `2026-07-14-operational-capability-views.md` — Home, Features, Connectors, Runs, and Memory projections.

---

## File structure

### New backend files

- `src/DigitalBrain.Kernel.Abstractions/Capabilities.cs` — serialized capability, resolution receipt, and safe proposal-reference contracts.
- `src/DigitalBrain.Kernel.Abstractions/FeatureHub.cs` — proposal state, transition API, and owner-scoped grain contract.
- `src/DigitalBrain.Kernel/Runtime/BuiltInCapabilityCatalog.cs` — explicit Gmail, Salesforce, Chat, and proposal-safe descriptors plus typed intent bindings.
- `src/DigitalBrain.Kernel/Runtime/HybridCapabilityResolver.cs` — structured filtering and deterministic exact/lexical/vector ranking.
- `src/DigitalBrain.Kernel/Runtime/FeatureHubGrain.cs` — encrypted durable proposal ownership.
- `tests/DigitalBrain.Tests/Runtime/BuiltInCapabilityCatalogTests.cs` — descriptor and binding contract tests.
- `tests/DigitalBrain.Tests/Runtime/HybridCapabilityResolverTests.cs` — ranking, fallback, ambiguity, and missing tests.
- `tests/DigitalBrain.Tests/Runtime/FeatureHubTransitionsTests.cs` — idempotency, bounds, and owner-state tests.
- `tests/DigitalBrain.Tests/Runtime/CapabilityWorkflowRunnerTests.cs` — runner integration tests.

### Modified backend files

- `src/DigitalBrain.Kernel.Abstractions/SemanticIntent.cs` — bind parameter extraction to a server-selected capability ID.
- `src/DigitalBrain.Kernel.Abstractions/ConversationModel.cs` — retain the grain API while accepting the selected capability in its request.
- `src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs` — durably retain safe capability and proposal receipts on the operation.
- `src/DigitalBrain.Core/DurableInoContracts.cs` — carry safe resolution metadata from workflow to the Orleans-owned operation.
- `src/DigitalBrain.Core/Conversation.cs` — project safe capability/proposal metadata to the native conversation snapshot.
- `src/DigitalBrain.Core/ConversationSurfacePayload.cs` — serialize the capability chip and proposal action.
- `src/DigitalBrain.Kernel/Runtime/ConversationModelGrain.cs` — extract parameters within the selected capability boundary.
- `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs` — resolve before model use and create proposals for missing actionable work.
- `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs` — persist the resolution receipt with the terminal operation transition.
- `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs` — pass the receipt through the state transition.
- `src/DigitalBrain.Mcp/ConversationStateClient.cs` — capture the authenticated grant snapshot when accepting a command.
- `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs` — register the catalog and resolver once per RuntimeHost process.
- `tests/DigitalBrain.Tests/Runtime/SemanticIntentModelTests.cs` — prove the model cannot select a different capability.
- `tests/DigitalBrain.Tests/Runtime/ConversationSurfacePayloadTests.cs` — prove bounded safe projection.
- `tests/DigitalBrain.Tests/Runtime/InoReminderHandoffTests.cs` — prove retry/recovery preserves the receipt.

### Modified Flutter files

- `app/lib/runtime/protocol/surface_protocol.dart` — parse capability and proposal receipts.
- `app/lib/runtime/widgets/ino_conversation_view.dart` — rename INO copy to Chat, render the capability chip, and render “Open Studio”.
- `app/lib/router.dart` — add a proposal-safe `/features/proposals/:proposalId` placeholder route owned by the next Studio plan.
- `app/test/runtime/surface_protocol_test.dart` — protocol parsing and rejection tests.
- `app/test/runtime/runtime_shell_test.dart` — capability chip and proposal action widget tests.

---

### Task 1: Define the capability catalog and typed bindings

**Files:**
- Create: `src/DigitalBrain.Kernel.Abstractions/Capabilities.cs`
- Create: `src/DigitalBrain.Kernel/Runtime/BuiltInCapabilityCatalog.cs`
- Create: `tests/DigitalBrain.Tests/Runtime/BuiltInCapabilityCatalogTests.cs`

**Interfaces:**
- Produces: `CapabilityDescriptor`, `CapabilityRisk`, `CapabilityOrigin`, `CapabilityResolutionKind`, `CapabilityResolutionReceipt`, `ICapabilityCatalog`, `CapabilityIntentBinding`, and `BuiltInCapabilityCatalog`.
- Consumes: existing `GmailTools`, `SalesforceTools`, `SemanticProvider`, and `SemanticOperation` constants.

- [ ] **Step 1: Write the catalog contract test**

```csharp
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

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
        Assert.Contains(descriptors, x => x.Id == GmailTools.ReadMessages);
        Assert.Contains(descriptors, x => x.Id == SalesforceTools.SearchRecords);
        Assert.Contains(descriptors, x => x.Id == "assistant.answer");
    }
}
```

- [ ] **Step 2: Run the root suite and verify the new test fails**

Run from the repository root in a background PowerShell job:

```powershell
$repo = (Get-Location).Path
$testJob = Start-Job -ScriptBlock { param($path) Set-Location $path; dotnet test --logger "console;verbosity=minimal" } -ArgumentList $repo
Wait-Job $testJob
Receive-Job $testJob
```

Expected: FAIL because `BuiltInCapabilityCatalog` and capability contracts do not exist.

- [ ] **Step 3: Add the public capability contracts**

Create `src/DigitalBrain.Kernel.Abstractions/Capabilities.cs` with these exact public shapes:

```csharp
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public enum CapabilityOrigin { Platform, Integration, Feature }
public enum CapabilityRisk { Read, InternalWrite, ExternalEffect }
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
    [property: Id(8)] CapabilityRisk Risk,
    [property: Id(9)] bool Available);

[GenerateSerializer, Alias("digitalbrain.capability.resolution-receipt.v1")]
public sealed record CapabilityResolutionReceipt(
    [property: Id(0)] CapabilityResolutionKind Kind,
    [property: Id(1)] string? CapabilityId,
    [property: Id(2)] string? CapabilityName,
    [property: Id(3)] string[] CandidateIds,
    [property: Id(4)] double Confidence);

[GenerateSerializer, Alias("digitalbrain.feature.proposal-reference.v1")]
public sealed record FeatureProposalReference(
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

Create `src/DigitalBrain.Kernel/Runtime/BuiltInCapabilityCatalog.cs`. Declare descriptors for `assistant.answer`, every currently routed `GmailTools` read/send ID, every currently routed `SalesforceTools` read/update ID, and `CrossProviderTools.MatchSalesforceAccountToGmailSender`. Each descriptor must have human examples, exact connection/grant requirements, and an explicit `CapabilityIntentBinding`:

```csharp
namespace DigitalBrain.Kernel.Runtime;

public sealed record CapabilityIntentBinding(SemanticProvider Provider, SemanticOperation Operation);

public sealed class BuiltInCapabilityCatalog : ICapabilityCatalog
{
    private static readonly IReadOnlyDictionary<string, CapabilityIntentBinding> Bindings =
        new Dictionary<string, CapabilityIntentBinding>(StringComparer.Ordinal)
        {
            ["assistant.answer"] = new(SemanticProvider.None, SemanticOperation.Answer),
            [GmailTools.ReadMessages] = new(SemanticProvider.Gmail, SemanticOperation.List),
            [GmailTools.ReadMailboxOverview] = new(SemanticProvider.Gmail, SemanticOperation.Overview),
            [GmailTools.ReadThreads] = new(SemanticProvider.Gmail, SemanticOperation.Threads),
            [GmailTools.Send] = new(SemanticProvider.Gmail, SemanticOperation.MutationPreview),
            [SalesforceTools.DiscoverObjects] = new(SemanticProvider.Salesforce, SemanticOperation.Discover),
            [SalesforceTools.SearchRecords] = new(SemanticProvider.Salesforce, SemanticOperation.Search),
            [SalesforceTools.ReadRecords] = new(SemanticProvider.Salesforce, SemanticOperation.List),
            [SalesforceTools.AggregateRecords] = new(SemanticProvider.Salesforce, SemanticOperation.Aggregate),
            [SalesforceTools.ContinueRecords] = new(SemanticProvider.Salesforce, SemanticOperation.NextPage),
            [SalesforceTools.UpdateRecord] = new(SemanticProvider.Salesforce, SemanticOperation.MutationPreview),
            [CrossProviderTools.MatchSalesforceAccountToGmailSender] = new(SemanticProvider.CrossProvider, SemanticOperation.Match)
        };

    private static readonly CapabilityDescriptor[] Descriptors = CapabilityDescriptorFactory.Create(Bindings.Keys);

    public IReadOnlyList<CapabilityDescriptor> Snapshot() => Descriptors;

    public static bool TryBind(string capabilityId, out CapabilityIntentBinding binding) =>
        Bindings.TryGetValue(capabilityId, out binding!);
}
```

Keep `CapabilityDescriptorFactory` internal in the same file. It must use a switch expression with one complete descriptor per binding key and throw for an unrecognized key. Do not derive IDs from CLR member names.

Use this complete factory shape, preserving the existing tool constants:

```csharp
internal static class CapabilityDescriptorFactory
{
    public static CapabilityDescriptor[] Create(IEnumerable<string> ids) => ids.Select(CreateOne).ToArray();

    private static CapabilityDescriptor CreateOne(string id) => id switch
    {
        "assistant.answer" => D(id, "Answer in Chat", "Answer an ordinary question without external data.",
            ["hello", "what can you do", "help me use DigitalBrain"]),
        GmailTools.ReadMessages => D(id, "Read Gmail messages", "List Gmail message metadata using bounded filters.",
            ["list my latest Gmail messages", "show unread inbox mail"], ["google"]),
        GmailTools.ReadMailboxOverview => D(id, "Read Gmail overview", "Read bounded mailbox totals.",
            ["how many unread emails do I have", "show my inbox overview"], ["google"]),
        GmailTools.ReadThreads => D(id, "Read Gmail threads", "List bounded Gmail thread metadata.",
            ["show recent email threads", "list unread Gmail conversations"], ["google"]),
        GmailTools.Send => D(id, "Send Gmail message", "Prepare one Gmail message for approval.",
            ["send an email", "email this update to Ada"], ["google"], ["gmail.send"], CapabilityRisk.ExternalEffect),
        SalesforceTools.DiscoverObjects => D(id, "Discover Salesforce objects", "List available Salesforce business objects.",
            ["what Salesforce objects are available", "discover CRM objects"], ["salesforce"]),
        SalesforceTools.SearchRecords => D(id, "Search Salesforce records", "Search Salesforce by a human business label.",
            ["find Acme in Salesforce", "search CRM accounts for Contoso"], ["salesforce"]),
        SalesforceTools.ReadRecords => D(id, "Read Salesforce records", "Read bounded Salesforce record fields.",
            ["list recent Salesforce accounts", "show account details"], ["salesforce"]),
        SalesforceTools.AggregateRecords => D(id, "Aggregate Salesforce records", "Calculate a bounded Salesforce aggregate.",
            ["count Salesforce accounts", "sum annual revenue by industry"], ["salesforce"]),
        SalesforceTools.ContinueRecords => D(id, "Continue Salesforce results", "Continue an existing Salesforce result page.",
            ["show the next Salesforce page", "continue those CRM results"], ["salesforce"]),
        SalesforceTools.UpdateRecord => D(id, "Update Salesforce field", "Prepare one Salesforce field change for approval.",
            ["update this Salesforce account description", "change one CRM field"], ["salesforce"], ["salesforce.write"], CapabilityRisk.ExternalEffect),
        CrossProviderTools.MatchSalesforceAccountToGmailSender => D(id, "Match Gmail sender to Salesforce", "Match a Gmail sender to a Salesforce account.",
            ["find the Salesforce account for this email sender", "match this Gmail sender to CRM"], ["google", "salesforce"]),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Capability binding has no descriptor.")
    };

    private static CapabilityDescriptor D(
        string id,
        string name,
        string description,
        string[] examples,
        string[]? connections = null,
        string[]? grants = null,
        CapabilityRisk risk = CapabilityRisk.Read) => new(
            id,
            1,
            name,
            description,
            examples,
            grants ?? [],
            connections ?? [],
            id == "assistant.answer" ? CapabilityOrigin.Platform : CapabilityOrigin.Integration,
            risk,
            true);
}
```

- [ ] **Step 5: Run the root suite and verify it passes**

Run the exact root job command from Step 2.

Expected: PASS with zero failed tests.

- [ ] **Step 6: Verify Aspire and commit**

Run `aspire doctor`, inspect `aspire resource list`, then commit:

```powershell
git add src/DigitalBrain.Kernel.Abstractions/Capabilities.cs src/DigitalBrain.Kernel/Runtime/BuiltInCapabilityCatalog.cs tests/DigitalBrain.Tests/Runtime/BuiltInCapabilityCatalogTests.cs
git commit -m "feat: define capability catalog"
```

---

### Task 2: Implement deterministic hybrid capability resolution

**Files:**
- Create: `src/DigitalBrain.Kernel/Runtime/HybridCapabilityResolver.cs`
- Create: `tests/DigitalBrain.Tests/Runtime/HybridCapabilityResolverTests.cs`
- Modify: `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs`

**Interfaces:**
- Consumes: `ICapabilityCatalog`, `IEmbeddingGenerator<string, Embedding<float>>`, and `CapabilitySearchRequest`.
- Produces: `HybridCapabilityResolver.ResolveAsync` implementing `ICapabilityResolver`.

- [ ] **Step 1: Write ranking, fallback, ambiguity, and filtering tests**

Create `HybridCapabilityResolverTests.cs` with four facts using a deterministic fake embedding generator:

```csharp
[Fact]
public async Task ResolveAsync_selects_company_search_from_semantic_similarity()
{
    var resolver = Resolver(new Dictionary<string, float[]>
    {
        ["Find Acme in our CRM"] = [1, 0],
        ["Search Salesforce records by a company or account name"] = [1, 0],
        ["Read recent Gmail messages"] = [0, 1]
    });

    var result = await resolver.ResolveAsync(Request("Find Acme in our CRM", connections: ["salesforce"]));

    Assert.Equal(CapabilityResolutionKind.Match, result.Receipt.Kind);
    Assert.Equal(SalesforceTools.SearchRecords, result.Receipt.CapabilityId);
}

[Fact]
public async Task ResolveAsync_falls_back_to_lexical_scoring_for_zero_vectors()
{
    var result = await ResolverWithZeroVectors().ResolveAsync(Request("list recent gmail messages", connections: ["google"]));

    Assert.Equal(GmailTools.ReadMessages, result.Receipt.CapabilityId);
}

[Fact]
public async Task ResolveAsync_returns_ambiguous_when_top_scores_are_too_close()
{
    var result = await ResolverWithEqualVectors().ResolveAsync(Request("show customer records", connections: ["salesforce"]));

    Assert.Equal(CapabilityResolutionKind.Ambiguous, result.Receipt.Kind);
    Assert.True(result.Receipt.CandidateIds.Length >= 2);
}

[Fact]
public async Task ResolveAsync_filters_missing_grants_before_scoring()
{
    var result = await ResolverWithExactVectors().ResolveAsync(Request("send an email", connections: ["google"]));

    Assert.DoesNotContain(GmailTools.Send, result.Receipt.CandidateIds);
    Assert.NotEqual(GmailTools.Send, result.Receipt.CapabilityId);
}
```

The shared `Request` helper must default grants and connections to empty `HashSet<string>(StringComparer.Ordinal)`. The fake generator must return configured vectors by exact input and `[0, 0]` otherwise.

- [ ] **Step 2: Run the root suite and verify failure**

Run the exact root job command from Task 1.

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
                ? 0.45 * x.Exact + 0.20 * x.Lexical + 0.35 * x.Vector
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

- [ ] **Step 5: Run the root suite and verify it passes**

Run the exact root job command from Task 1.

Expected: PASS with zero failed tests, including all four resolver cases.

- [ ] **Step 6: Verify Aspire and commit**

```powershell
git add src/DigitalBrain.Kernel/Runtime/HybridCapabilityResolver.cs src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs tests/DigitalBrain.Tests/Runtime/HybridCapabilityResolverTests.cs
git commit -m "feat: resolve capabilities with hybrid search"
```

---

### Task 3: Constrain model extraction to the selected capability

**Files:**
- Modify: `src/DigitalBrain.Kernel.Abstractions/SemanticIntent.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/ConversationModelGrain.cs`
- Modify: `tests/DigitalBrain.Tests/Runtime/SemanticIntentModelTests.cs`

**Interfaces:**
- Consumes: `BuiltInCapabilityCatalog.TryBind` and `SemanticIntentRequest.CapabilityId`.
- Produces: a `SemanticIntentProposal` whose provider and operation must match the selected descriptor binding.

- [ ] **Step 1: Add a failing model-boundary test**

Add a test in `SemanticIntentModelTests.cs` that configures the recording model to return Salesforce Search for a Gmail-selected request:

```csharp
[Fact]
public async Task ResolveIntentAsync_rejects_a_model_selected_capability_change()
{
    var chat = new RecordingStructuredChatClient(new SemanticIntentProposal(
        SemanticProvider.Salesforce,
        SemanticOperation.Search,
        SearchText: "Acme"));
    var grain = new ConversationModelGrain(chat);

    await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ResolveIntentAsync(new SemanticIntentRequest(
        ActorScope,
        ActorScope,
        "conversation-1",
        "list recent mail",
        [],
        GmailTools.ReadMessages)));
}
```

- [ ] **Step 2: Run the root suite and verify failure**

Run the exact root job command from Task 1.

Expected: FAIL because `SemanticIntentRequest` has no selected capability field and the grain does not validate it.

- [ ] **Step 3: Add the serialized request field**

Append this field to `SemanticIntentRequest` without renumbering existing fields:

```csharp
[property: Id(5)] string CapabilityId
```

Update every existing constructor call to pass the server-selected ID. Existing tests that bypass resolution must pass the capability matching their expected provider and operation.

- [ ] **Step 4: Bind and validate model output**

At the start of `ConversationModelGrain.ResolveIntentAsync`, resolve the binding and reject an unknown ID. Replace open provider-choice guidance with capability-specific guidance. After structured output, reject any provider or operation that differs:

```csharp
if (!BuiltInCapabilityCatalog.TryBind(request.CapabilityId, out var binding))
    throw new ArgumentException("The selected capability is unknown.", nameof(request));

var proposal = response.Result ?? throw new InvalidOperationException("The intent model returned no structured proposal.");
if (proposal.Provider != binding.Provider || proposal.Operation != binding.Operation)
    throw new InvalidOperationException("The intent model changed the selected capability.");
return proposal;
```

`IntentGuidance` must receive the binding and capability ID, state both as fixed server decisions, and retain the existing rules for filters, ordinals, time ranges, and provider-safe values.

- [ ] **Step 5: Run the root suite and verify it passes**

Run the exact root job command from Task 1.

Expected: PASS with zero failed tests.

- [ ] **Step 6: Verify Aspire and commit**

```powershell
git add src/DigitalBrain.Kernel.Abstractions/SemanticIntent.cs src/DigitalBrain.Kernel/Runtime/ConversationModelGrain.cs tests/DigitalBrain.Tests/Runtime/SemanticIntentModelTests.cs tests/DigitalBrain.Tests/Runtime/TypedReadWorkflowRunnerTests.cs tests/DigitalBrain.Tests/Runtime/AgentFrameworkWorkflowRunnerTests.cs
git commit -m "refactor: bind intent extraction to capabilities"
```

---

### Task 4: Route Chat through the capability resolver

**Files:**
- Modify: `src/DigitalBrain.Core/DurableInoContracts.cs`
- Modify: `src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Mcp/ConversationStateClient.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs`
- Create: `tests/DigitalBrain.Tests/Runtime/CapabilityWorkflowRunnerTests.cs`

**Interfaces:**
- Consumes: `ICapabilityResolver`, `CapabilitySearchRequest`, and the selected typed binding.
- Produces: `InoWorkflowResult.Capability` for match, ambiguity, and missing outcomes.

- [ ] **Step 1: Write failing runner tests**

Create three tests:

```csharp
[Fact]
public async Task ExecuteAsync_resolves_before_calling_the_parameter_model()
{
    var resolver = new RecordingCapabilityResolver(Match(GmailTools.ReadMessages, "Read Gmail messages"));
    var runner = Runner(resolver);

    var result = await runner.ExecuteAsync(Request("list my latest messages"));

    Assert.Equal(1, resolver.CallCount);
    Assert.Equal(GmailTools.ReadMessages, result.Capability?.CapabilityId);
    Assert.Equal(GmailTools.ReadMessages, Model.LastRequest?.CapabilityId);
}

[Fact]
public async Task ExecuteAsync_returns_clarification_for_ambiguous_capabilities()
{
    var runner = Runner(new RecordingCapabilityResolver(Ambiguous(GmailTools.ReadMessages, GmailTools.ReadThreads)));

    var result = await runner.ExecuteAsync(Request("show my mail"));

    Assert.Equal(CapabilityResolutionKind.Ambiguous, result.Capability?.Kind);
    Assert.Contains("choose", result.Text, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(0, Model.IntentCalls);
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

Run the exact root job command from Task 1.

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
    FeatureProposalReference? Proposal = null);
```

Do not add embeddings, prompts, provider payloads, or candidate descriptions to this durable boundary.

- [ ] **Step 4: Capture the authenticated grant snapshot**

Append an Orleans field to `AcceptedCommand` and `ConversationOperation` without changing existing IDs:

```csharp
[property: Id(9)] string[] Grants
```

```csharp
[property: Id(16)] string[] Grants
```

Append grants to the in-process workflow request:

```csharp
public sealed record InoWorkflowRequest(
    string OperationId,
    string ConversationId,
    string Prompt,
    IReadOnlyList<string> History,
    string RequestId,
    InoAuthorizationResume? AuthorizationResume = null,
    WorkflowReference? PriorWorkflow = null,
    string? ActorScope = null,
    IReadOnlyList<string>? Grants = null);
```

`ConversationStateClient.BeginAsync` must sort and copy `RequestContext.Grants` into `AcceptedCommand`. `ConversationTransitions.BeginOperation` must copy that array into `ConversationOperation`. `InoOperationWorkerGrain` must pass `claimed.Grants` to `InoWorkflowRequest`. Validation must reject null entries, control characters, duplicates, more than 64 grants, and grant strings longer than 128 characters. This snapshot controls discovery only; the existing effect gate remains authoritative for execution and revocation.

- [ ] **Step 5: Resolve before parameter extraction**

In `TryExecuteTypedReadAsync`, resolve once with the bounded prompt. Build the search context from server-known grants and composed connections. For this slice, the composed connection set is `google` and `salesforce`; grants remain the exact session grants already supplied to the worker. Pass those grants through `InoWorkflowRequest` as an immutable string array populated by `InoOperationWorkerGrain` from the authoritative session state, not from Flutter input.

Add this current-composition constant beside the runner bounds; the Feature runtime plan replaces it with owner-scoped installed Feature contributions:

```csharp
private static readonly IReadOnlySet<string> ComposedIntegrationIds =
    new HashSet<string>(["google", "salesforce"], StringComparer.Ordinal);
```

Use this control flow:

```csharp
var search = new CapabilitySearchRequest(
    request.Prompt,
    new HashSet<string>(request.Grants ?? [], StringComparer.Ordinal),
    ComposedIntegrationIds,
    3);
var resolution = await resolver.ResolveAsync(search, cancellationToken).ConfigureAwait(false);
if (resolution.Receipt.Kind == CapabilityResolutionKind.Ambiguous)
    return new InoWorkflowResult(
        "I found more than one capability that could handle this. Please choose the intended result.",
        workflow,
        Capability: resolution.Receipt);
if (resolution.Receipt.Kind == CapabilityResolutionKind.Missing)
    return await CreateMissingCapabilityResultAsync(request, workflow, resolution.Receipt, cancellationToken);
var selected = resolution.Selected ?? throw new InvalidOperationException("A matched capability has no descriptor.");
var intent = await model.ResolveIntentAsync(new SemanticIntentRequest(
    request.ActorScope,
    request.ActorScope,
    request.ConversationId,
    request.Prompt,
    [],
    selected.Id), cancellationToken).ConfigureAwait(false);
```

`ComposedIntegrationIds` contains only provider packages registered in RuntimeHost, not OAuth connection state. The `assistant.answer` match returns control to the existing bounded Chat path. All integration and cross-provider matches continue through typed dispatch and the existing connection and effect rails.

- [ ] **Step 6: Run the root suite and verify it passes**

Run the exact root job command from Task 1.

Expected: PASS with zero failed tests and no behavior regression in typed Gmail/Salesforce tests.

- [ ] **Step 7: Verify Aspire and commit**

```powershell
git add src/DigitalBrain.Core/DurableInoContracts.cs src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs src/DigitalBrain.Mcp/ConversationStateClient.cs tests/DigitalBrain.Tests/Runtime/CapabilityWorkflowRunnerTests.cs tests/DigitalBrain.Tests/Runtime/TypedReadWorkflowRunnerTests.cs
git commit -m "feat: resolve chat requests through capabilities"
```

---

### Task 5: Persist idempotent missing-capability proposals

**Files:**
- Create: `src/DigitalBrain.Kernel.Abstractions/FeatureHub.cs`
- Create: `src/DigitalBrain.Kernel/Runtime/FeatureHubGrain.cs`
- Create: `tests/DigitalBrain.Tests/Runtime/FeatureHubTransitionsTests.cs`
- Modify: `src/DigitalBrain.Kernel.Abstractions/RuntimeState.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs`

**Interfaces:**
- Produces: `FeatureProposal`, `FeatureHubState`, `FeatureHubTransitions.CreateProposal`, and `IFeatureHubGrain`.
- Consumes: owner scope, operation ID, original prompt, and `TimeProvider`.

- [ ] **Step 1: Write transition tests**

```csharp
[Fact]
public void CreateProposal_is_idempotent_for_the_same_operation()
{
    var request = new CreateFeatureProposal("operation-1", "Research Acme and create a text file", Now);

    var first = FeatureHubTransitions.CreateProposal(FeatureHubState.Empty(), OwnerScope, request);
    var second = FeatureHubTransitions.CreateProposal(first.State, OwnerScope, request);

    Assert.Equal(first.Proposal, second.Proposal);
    Assert.Same(first.State, second.State);
}

[Fact]
public void CreateProposal_rejects_unbounded_or_control_character_prompts()
{
    Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateProposal(
        FeatureHubState.Empty(),
        OwnerScope,
        new CreateFeatureProposal("operation-1", new string('x', 4097), Now)));
    Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateProposal(
        FeatureHubState.Empty(),
        OwnerScope,
        new CreateFeatureProposal("operation-2", "unsafe\u0000prompt", Now)));
}
```

- [ ] **Step 2: Run the root suite and verify failure**

Run the exact root job command from Task 1.

Expected: FAIL because Feature Hub contracts and transitions do not exist.

- [ ] **Step 3: Add Feature Hub contracts and pure transitions**

Create `FeatureHub.cs` with serialized state and grain contracts. Use these fields:

```csharp
[GenerateSerializer, Alias("digitalbrain.feature.proposal.v1")]
public sealed record FeatureProposal(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string Goal,
    [property: Id(3)] string Status,
    [property: Id(4)] DateTimeOffset CreatedAt);

[GenerateSerializer, Alias("digitalbrain.feature.hub-state.v1")]
public sealed record FeatureHubState(
    [property: Id(0)] int SchemaVersion,
    [property: Id(1)] long Revision,
    [property: Id(2)] FeatureProposal[] Proposals)
{
    public static FeatureHubState Empty() => new(RuntimeStateSchemas.FeatureHub, 0, []);
}

[Alias("digitalbrain.feature.hub-grain.v1")]
public interface IFeatureHubGrain : IGrainWithStringKey
{
    [Alias("digitalbrain.feature.proposal.create.v1")]
    Task<FeatureProposal> CreateProposalAsync(CreateFeatureProposal request);
}
```

`FeatureHubTransitions` must cap proposals at 100 per owner, prompts at 4096 characters, and proposal labels at 80 characters. Derive `ProposalId` as `proposal-` plus the first 32 lowercase hex characters of SHA-256 over `ownerScope + "\0" + operationId`. Store the bounded original goal; do not generate source, permissions, or triggers in this slice.

- [ ] **Step 4: Implement encrypted durable ownership**

Create `FeatureHubGrain.cs` using the existing `EncryptedPersistentState<FeatureHubState>` pattern with `[PersistentState("feature-hub", RuntimeStateStorageProviders.GrainState)]`. Add `RuntimeStateSchemas.FeatureHub` and `RuntimeStateKinds.FeatureHub` without changing existing numeric schema values.

- [ ] **Step 5: Create proposals only for missing actionable work**

In `AgentFrameworkWorkflowRunner.CreateMissingCapabilityResultAsync`, classify a missing request as ordinary conversation only when the prompt is a greeting, thanks, help request, or capability question covered by `assistant.answer`. For any other missing request, call the owner-scoped `IFeatureHubGrain` idempotently and return:

```csharp
return new InoWorkflowResult(
    "I don’t have a trusted capability for that yet. I created a Feature draft. Open Studio to define and verify its behavior?",
    workflow,
    Capability: receipt,
    Proposal: new FeatureProposalReference(
        proposal.ProposalId,
        "Open Studio",
        "/features/proposals/" + proposal.ProposalId));
```

Do not call a model to decide whether code should be installed.

- [ ] **Step 6: Run the root suite and verify it passes**

Run the exact root job command from Task 1.

Expected: PASS with zero failed tests.

- [ ] **Step 7: Verify Aspire and commit**

```powershell
git add src/DigitalBrain.Kernel.Abstractions/FeatureHub.cs src/DigitalBrain.Kernel.Abstractions/RuntimeState.cs src/DigitalBrain.Kernel/Runtime/FeatureHubGrain.cs src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs tests/DigitalBrain.Tests/Runtime/FeatureHubTransitionsTests.cs tests/DigitalBrain.Tests/Runtime/CapabilityWorkflowRunnerTests.cs
git commit -m "feat: persist feature proposals for missing capabilities"
```

---

### Task 6: Persist and project safe capability receipts

**Files:**
- Modify: `src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Core/Conversation.cs`
- Modify: `src/DigitalBrain.Core/DurableInoContracts.cs`
- Modify: `src/DigitalBrain.Core/ConversationSurfacePayload.cs`
- Modify: `tests/DigitalBrain.Tests/Runtime/ConversationSurfacePayloadTests.cs`
- Modify: `tests/DigitalBrain.Tests/Runtime/InoReminderHandoffTests.cs`

**Interfaces:**
- Consumes: `InoWorkflowResult.Capability` and `.Proposal`.
- Produces: optional `capability` and `proposal` objects in the native `inoConversation` payload.

- [ ] **Step 1: Add failing projection and recovery tests**

Add a payload test asserting exact safe JSON:

```csharp
Assert.Equal("salesforce.search.records", operation.GetProperty("capability").GetProperty("id").GetString());
Assert.Equal("Search Salesforce records", operation.GetProperty("capability").GetProperty("name").GetString());
Assert.Equal("match", operation.GetProperty("capability").GetProperty("kind").GetString());
Assert.Equal("proposal-0123456789abcdef0123456789abcdef", operation.GetProperty("proposal").GetProperty("id").GetString());
Assert.Equal("/features/proposals/proposal-0123456789abcdef0123456789abcdef", operation.GetProperty("proposal").GetProperty("route").GetString());
Assert.DoesNotContain("prompt", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
```

Add a reminder handoff test that persists, deactivates, and reloads an operation carrying both receipts, then asserts equality.

- [ ] **Step 2: Run the root suite and verify failure**

Run the exact root job command from Task 1.

Expected: FAIL because operation state and surface payload do not retain the receipts.

- [ ] **Step 3: Append serialized operation fields**

Append these optional fields to `ConversationOperation` after the grant snapshot added in Task 4:

```csharp
[property: Id(17)] CapabilityResolutionReceipt? Capability = null,
[property: Id(18)] FeatureProposalReference? Proposal = null
```

Thread them through `CompleteWithAssistantAsync`, `ConversationTransitions.CompleteWithAssistant`, and the worker completion call. Existing callers pass null by default. Add validation that receipt IDs, names, labels, routes, confidence, and candidate counts are bounded and that proposal routes begin with `/features/proposals/proposal-`.

- [ ] **Step 4: Project only safe fields**

In `ConversationSurfacePayload.Build`, emit:

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

Run the exact root job command from Task 1.

Expected: PASS with zero failed tests.

- [ ] **Step 6: Verify Aspire and commit**

```powershell
git add src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs src/DigitalBrain.Core/Conversation.cs src/DigitalBrain.Core/DurableInoContracts.cs src/DigitalBrain.Core/ConversationSurfacePayload.cs tests/DigitalBrain.Tests/Runtime/ConversationSurfacePayloadTests.cs tests/DigitalBrain.Tests/Runtime/InoReminderHandoffTests.cs
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
      'id': 'salesforce.search.records',
      'name': 'Search Salesforce records',
      'confidence': 0.91,
    },
    proposal: {
      'id': 'proposal-0123456789abcdef0123456789abcdef',
      'label': 'Open Studio',
      'route': '/features/proposals/proposal-0123456789abcdef0123456789abcdef',
    },
  ));

  expect(payload.operation!.capability!.id, 'salesforce.search.records');
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

Run from `app`:

```powershell
flutter test
```

Expected: FAIL because receipt models and parsing do not exist.

- [ ] **Step 3: Add strict protocol models**

In `surface_protocol.dart`, add immutable `InoCapabilityReceipt` and `InoFeatureProposalReference` classes with strict `fromWire` factories. Bound IDs to 128 characters, names/labels to 80, candidates to 5, confidence to `0..1`, and routes to the exact internal proposal prefix. Append optional fields to `InoConversationOperation` without changing existing required fields.

- [ ] **Step 4: Render product language and actions**

In `ino_conversation_view.dart`:

- change the semantic label from `INO conversation` to `Chat conversation`;
- change `Ask INO` to `Chat`;
- render a small capability chip above `_OperationStatus` when a receipt has an ID and name;
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

Run `flutter test` from `app`.

Expected: PASS with zero failed tests.

- [ ] **Step 8: Verify Aspire and commit**

```powershell
git add app/lib/runtime/protocol/surface_protocol.dart app/lib/runtime/widgets/ino_conversation_view.dart app/lib/runtime/widgets/feature_proposal_placeholder.dart app/lib/router.dart app/test/runtime/surface_protocol_test.dart app/test/runtime/runtime_shell_test.dart
git commit -m "feat: show capabilities and feature proposals in chat"
```

---

### Task 8: Prove the vertical slice end to end

**Files:**
- Modify: `tests/DigitalBrain.Tests/Runtime/RuntimeSurfaceFeedTests.cs`
- Modify: `tests/DigitalBrain.Tests/Runtime/UiGrpcServiceTests.cs`
- Modify: `app/test/runtime/grpc_ui_transport_test.dart`

**Interfaces:**
- Consumes: the completed capability resolution, proposal persistence, feed projection, gRPC transport, and Flutter parsing path.
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

Repeat delivery with the same operation ID and assert exactly one proposal in `FeatureHubState`.

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

- [ ] **Step 5: Commit the acceptance proof**

```powershell
git add tests/DigitalBrain.Tests/Runtime/RuntimeSurfaceFeedTests.cs tests/DigitalBrain.Tests/Runtime/UiGrpcServiceTests.cs app/test/runtime/grpc_ui_transport_test.dart
git commit -m "test: prove capability proposal flow end to end"
```

## Completion gate

This plan is complete only when all eight task commits exist, the exact root .NET suite and full Flutter suite pass, Aspire is healthy, and a live Company Research request creates one durable proposal without calling the general Chat model or installing code.

The next plan begins from that proposal ID and replaces the Flutter placeholder with the living BDD and single-file C# Studio workflow.
