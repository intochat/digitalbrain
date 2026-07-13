# BrainProgramming.md

Refined architecture for self-programmable integrations (Salesforce first, Gmail second) plus the test story that makes it safe to let a script — human or LLM authored — touch either one.

## 1. The complaint, restated

`ISalesforceReadToolGrain` + `ISalesforceMutationToolGrain` + `SalesforceTools` (string constants) is three places to look for one capability. IAW solves the same problem with one interface: `IDotNet`, `IRoslyn`. One file, one contract, static metadata living next to the methods it describes. That's the shape to copy — not IAW's `Agent<T>` runtime (DigitalBrain's journaled `Neuron` is a deliberately more advanced model; don't downgrade to copy a simpler one), just the **interface shape**.

## 2. Compare the two patterns

| | DigitalBrain today | IAW | Proposed |
|---|---|---|---|
| Interfaces per integration | 2–3 (`Read`/`Mutation`/`Metadata` grains) | 1 (`IDotNet`, `IRoslyn`) | 1 |
| Tool identity | hand-maintained string constants (`SalesforceTools.ReadRecords`) | derived from interface method + `[Description]` | derived, constants deleted |
| Agent metadata | none — no LLM-facing description on the interface itself | static interface members (`AgentDisplayName`, `AgentCapabilities`, `AgentInstructions`) | adopted |
| Read "presets" | 5 separate grain methods (`ReadLatestAccountAsync`, `ReadCurrentProfileAsync`, `ReadRecentAccountsAsync`, `ReadRecentContactsAsync`, `ReadCrmSchemaAsync`) | N/A | 1 method + static factories on the query record |
| Mutation safety | two-phase preview/apply, split across a second grain interface | N/A | two-phase preview/apply, same interface |
| Why it matters for self-programming | a generated script has to get 2–3 interfaces and a constants class right | a generated script targets 1 interface | fewer types = less surface for an LLM script to hallucinate against, and `OrchestrationCompiler`-style compile-checking has less to validate |

The last row is the real payoff: this isn't cleanup for its own sake, it directly reduces the failure surface for the self-programming pipeline from the last round of this brainstorm.

## 3. The new contract

One small addition to `DigitalBrain.Kernel.Abstractions` — borrowed shape, not borrowed runtime:

```csharp
public interface IToolAgent
{
    static abstract string AgentDisplayName { get; }
    static abstract string AgentDescription { get; }
    static abstract string[] AgentCapabilities { get; }
    static abstract string AgentInstructions { get; }
}
```

Then Salesforce collapses to one interface:

```csharp
namespace DigitalBrain.Kernel.Runtime;

[Alias("digitalbrain.v3.salesforce")]
public interface ISalesforce : IToolAgent, IGrainWithStringKey
{
    static string IToolAgent.AgentDisplayName => "Salesforce";

    static string IToolAgent.AgentDescription =>
        "Reads, searches, and safely mutates Salesforce CRM records for the tenant's connected org.";

    static string[] IToolAgent.AgentCapabilities =>
        ["crm", "salesforce", "read", "search", "aggregate", "mutate"];

    static string IToolAgent.AgentInstructions => """
        Use ReadAsync for records, SearchAsync for free text, AggregateAsync for rollups.
        Mutations are always two-phase: PreviewMutationAsync before ApplyMutationAsync.
        Never call ApplyMutationAsync without a SalesforcePreparedMutation from a prior preview.
        """;

    Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(CancellationToken ct = default);
    Task<SalesforceResult> BeginAuthorizationAsync(string startToken, CancellationToken ct = default);
    Task<AuthResult> CompleteAuthorizationAsync(OAuthCallback callback, CancellationToken ct = default);

    Task<SalesforceResult> ReadAsync(SalesforceQuery query, CancellationToken ct = default);
    Task<SalesforceResult> SearchAsync(string text, int limit = 20, CancellationToken ct = default);
    Task<SalesforceResult> AggregateAsync(SalesforceAggregate spec, CancellationToken ct = default);

    Task<SalesforceMutationPreview> PreviewMutationAsync(SalesforceMutation mutation, CancellationToken ct = default);
    Task<SalesforceMutationResult> ApplyMutationAsync(SalesforcePreparedMutation prepared, CancellationToken ct = default);
}
```

`ExternalAuthorizationResolution`, `AuthResult`, `OAuthCallback` already exist as shared contracts — reused, not redefined.

## 4. Records: from ~20 down to 9

```csharp
public enum SalesforceStatus
{
    Success, NeedsAuth, ConfigurationMissing, Unavailable,
    AccessDenied, LimitReached, InvalidRequest, ContinuationExpired
}

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-entity")]
public sealed record SalesforceEntity([property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-field")]
public sealed record SalesforceField([property: Id(0)] string Label);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-filter")]
public sealed record SalesforceFilter(
    [property: Id(0)] SalesforceField Field,
    [property: Id(1)] SemanticFilterOperator Operator,
    [property: Id(2)] string? Value = null);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-query")]
public sealed record SalesforceQuery(
    [property: Id(0)] SalesforceEntity Entity,
    [property: Id(1)] IReadOnlyList<SalesforceField>? Fields = null,
    [property: Id(2)] IReadOnlyList<SalesforceFilter>? Filters = null,
    [property: Id(3)] string? RecordId = null,
    [property: Id(4)] string? Continuation = null,
    [property: Id(5)] int Limit = 20)
{
    public static SalesforceQuery LatestAccount() => new(new SalesforceEntity("Account"), Limit: 1);
    public static SalesforceQuery CurrentProfile() => new(new SalesforceEntity("User"), Limit: 1);
    public static SalesforceQuery RecentContacts(int limit = 20) => new(new SalesforceEntity("Contact"), Limit: limit);
    public static SalesforceQuery RecentAccounts(int limit = 20) => new(new SalesforceEntity("Account"), Limit: limit);
    public static SalesforceQuery CrmSchema() => new(new SalesforceEntity("EntityDefinition"), Limit: 50);
}

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-aggregate")]
public sealed record SalesforceAggregate(
    [property: Id(0)] SalesforceEntity Entity,
    [property: Id(1)] SemanticAggregateFunction Function,
    [property: Id(2)] SalesforceField? Field = null,
    [property: Id(3)] SalesforceField? GroupBy = null,
    [property: Id(4)] IReadOnlyList<SalesforceFilter>? Filters = null);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-mutation")]
public sealed record SalesforceMutation(
    [property: Id(0)] SalesforceEntity Entity,
    [property: Id(1)] string RecordId,
    [property: Id(2)] SalesforceField Field,
    [property: Id(3)] string NewValue);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-prepared-mutation")]
public sealed record SalesforcePreparedMutation([property: Id(0)] byte[] Payload);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-mutation-preview")]
public sealed record SalesforceMutationPreview(
    [property: Id(0)] SalesforceStatus Status,
    [property: Id(1)] string? OriginalValue = null,
    [property: Id(2)] SalesforcePreparedMutation? Prepared = null,
    [property: Id(3)] string? SafeReason = null);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-mutation-result")]
public sealed record SalesforceMutationResult(
    [property: Id(0)] SalesforceStatus Status,
    [property: Id(1)] string? SafeReason = null);

[GenerateSerializer, Alias("digitalbrain.v3.salesforce-result")]
public sealed record SalesforceResult(
    [property: Id(0)] SalesforceStatus Status,
    [property: Id(1)] string? Content = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null,
    [property: Id(4)] string? Continuation = null,
    [property: Id(5)] int ReturnedCount = 0,
    [property: Id(6)] int? TotalSize = null);
```

`[GenerateSerializer]` + sequential `[Id(n)]` stays — that's a Core Law, not negotiable boilerplate. What's deleted is the *count* of types needing it, not the discipline itself.

## 5. What gets deleted

| Delete | Replaced by |
|---|---|
| `ISalesforceReadToolGrain` | folded into `ISalesforce` |
| `ISalesforceMutationToolGrain` | folded into `ISalesforce` |
| `SalesforceTools` string constants | tool identity derived from method name + `[Alias]`, same as IAW |
| `ReadLatestAccountAsync`, `ReadCurrentProfileAsync`, `ReadRecentAccountsAsync`, `ReadRecentContactsAsync`, `ReadCrmSchemaAsync` | `ReadAsync(SalesforceQuery.LatestAccount())` etc. |
| `SalesforceDiscoveryRequest`, `SalesforceSearchRequest`, `SalesforceRecordReadRequest`, `SalesforceContinuationRequest` | one `SalesforceQuery` |
| `SalesforceSemanticEntity` / `SalesforceSemanticField` / `SalesforceResolvedRecord` / `SalesforceSort` | `SalesforceEntity` / `SalesforceField` (sort dropped until something actually needs it — add back only when a real caller does) |
| `SalesforceReadScope`, `SalesforceContinuation` as separate records | scope comes from grain key (tenant/org already implicit in `IGrainWithStringKey`'s key); continuation is a string on `SalesforceResult`/`SalesforceQuery` |

Same treatment applies to Gmail (`IGmailReadToolGrain` + `IGmailMutationToolGrain` + `IGmailMetadataToolGrain` → `IGmail`) once Salesforce proves out — don't do both at once.

## 6. Testability — the part that actually matters

Split every grain into a thin Orleans shell and a pure, Orleans-free core, the same separation `ConversationTransitions` already uses successfully for conversation state:

```csharp
public static class SalesforceOperations
{
    public static SalesforceQuery Normalize(SalesforceQuery query) =>
        query.Limit is < 1 or > 200 ? query with { Limit = 20 } : query;

    public static SalesforceMutationPreview Preview(SalesforceMutation mutation, string currentValue) =>
        new(SalesforceStatus.Success, currentValue, new SalesforcePreparedMutation(Encode(mutation)));

    private static byte[] Encode(SalesforceMutation mutation) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(mutation);
}
```

```csharp
public sealed class SalesforceGrain(ISalesforceClient client) : Grain, ISalesforce
{
    public async Task<SalesforceResult> ReadAsync(SalesforceQuery query, CancellationToken ct = default)
    {
        var normalized = SalesforceOperations.Normalize(query);
        var raw = await client.QueryAsync(normalized, ct);
        return SalesforceOperations.ToResult(raw);
    }

    public async Task<SalesforceMutationPreview> PreviewMutationAsync(SalesforceMutation mutation, CancellationToken ct = default)
    {
        var current = await client.ReadFieldAsync(mutation.Entity, mutation.RecordId, mutation.Field, ct);
        return SalesforceOperations.Preview(mutation, current);
    }
}
```

`SalesforceOperations` is a plain static class — zero Orleans, zero network, `dotnet test` runs it in milliseconds:

```csharp
public class SalesforceOperationsTests
{
    [Fact]
    public void LatestAccount_TargetsAccountEntity()
    {
        var query = SalesforceQuery.LatestAccount();
        Assert.Equal("Account", query.Entity.Label);
        Assert.Equal(1, query.Limit);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeLimit()
    {
        var normalized = SalesforceOperations.Normalize(new SalesforceQuery(new SalesforceEntity("Contact"), Limit: 500));
        Assert.Equal(20, normalized.Limit);
    }
}
```

For the full scenario ("new email → analyze → Salesforce") from the earlier brainstorm, this is where `dotnet run app.cs` closes the loop — one file, `Orleans.TestingHost` for a real (in-memory) cluster, `ISalesforce` as the only surface a generated script needs to know:

```csharp
#:package xunit.v3@1.*
#:package Orleans.TestingHost@9.*

using Orleans.TestingHost;
using Xunit;

var cluster = new TestClusterBuilder().Build();
await cluster.DeployAsync();

var salesforce = cluster.GrainFactory.GetGrain<ISalesforce>("test-tenant");
var mutation = new SalesforceMutation(new SalesforceEntity("Contact"), "003xx", new SalesforceField("Email"), "new@example.com");
var preview = await salesforce.PreviewMutationAsync(mutation);

Assert.Equal(SalesforceStatus.Success, preview.Status);
Assert.NotNull(preview.Prepared);

var applied = await salesforce.ApplyMutationAsync(preview.Prepared!);
Assert.Equal(SalesforceStatus.Success, applied.Status);

await cluster.StopAllSilosAsync();
```

Applying happens through `IConversationNeuron`'s existing `RequestApprovalWithAssistantAsync` / `DecideApprovalWithAssistantAsync` state machine in production — the scenario test above exercises the tool in isolation, a separate (already-existing) test covers the approval gate.

## 7. What NOT to touch

- `IOAuthProviderAdapter` stays in `DigitalBrain.Core`, not `DigitalBrain.Kernel` — the existing trap still applies, this refactor doesn't move auth code.
- No new project. `ISalesforce` and its records live in `DigitalBrain.Kernel.Abstractions` exactly where the old interfaces lived.
- `FoundryCompilation`, `PackAlcEmbodier`, `CapabilityGate`, `MarketplaceNeuron` are untouched — a simplified `ISalesforce` is *easier* for those to embody, not a reason to touch them now.
- Journal/checkpoint/branch machinery on `Neuron` is untouched — this refactor is scoped to the tool-grain layer, not the neuron runtime.

## 8. Migration order (small, reversible steps)

1. Add `IToolAgent` to `DigitalBrain.Kernel.Abstractions`.
2. Add `ISalesforce` + the 9 records alongside the existing types (don't delete yet).
3. Implement `SalesforceGrain` + `SalesforceOperations`, with the unit tests in §6 passing before anything else changes.
4. Point one real caller at `ISalesforce`, verify end to end.
5. Only then delete `ISalesforceReadToolGrain`, `ISalesforceMutationToolGrain`, `SalesforceTools`, and the now-redundant request records.
6. Repeat for Gmail once step 5 is done and stable — not in parallel.

## 9. Open decisions for Vlad

- Does `SalesforceSort` have a real caller today, or was it speculative? (Proposal above drops it.)
- Should `IToolAgent` also grow a `Task<string[]> DescribeCapabilitiesAsync()` for runtime introspection by the self-programming pipeline, or is the static interface metadata enough for the LLM tool-router as-is?
- Same question for Gmail: one `IGmail`, or does `SendAsync`'s side-effect profile deserve staying separate from the three read methods for approval-gating reasons the way mutation is split from read here?
