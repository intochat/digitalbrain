using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using Brain.Tests.Kernel;
using DigitalBrain.Salesforce;
using Xunit;

namespace Brain.Tests.Salesforce;

public sealed class SalesforceNeuronTests
{
    private static readonly NeuronAddress Self = new(
        new OrganizationId("org-1"),
        new SpaceId("space-1"),
        "salesforce.v1",
        "sf-1");

    private static SynapseMetadata Meta(Guid commandId) =>
        new(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: new OrganizationId("org-1"),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: new SpaceId("space-1"),
            Source: Self,
            SourceSequence: 1,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

    private static (SalesforceReactiveCore Core, FakeSalesforceMcpClient Mcp, OrderingReactiveStore Store) CreateCore()
    {
        var store = new OrderingReactiveStore();
        var mcp = new FakeSalesforceMcpClient();
        mcp.OnUpdate = () => store.Order.Add("provider");
        mcp.OnQuery = () => store.Order.Add("provider");
        var core = new SalesforceReactiveCore(store, mcp, Self);
        return (core, mcp, store);
    }

    [Fact]
    public void Salesforce_contract_exposes_only_typed_operations()
    {
        var methods = typeof(ISalesforce).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.QueryRecordsAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.UpdateRecordAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.GetSurfaceAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.GetIdentityAsync));

        Assert.Equal(
            typeof(CommandSynapse<SalesforceQueryRequest>),
            typeof(ISalesforce).GetMethod(nameof(ISalesforce.QueryRecordsAsync))!.GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(CommandSynapse<SalesforceUpdateRequest>),
            typeof(ISalesforce).GetMethod(nameof(ISalesforce.UpdateRecordAsync))!.GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(Task<UiSurfaceSnapshot>),
            typeof(ISalesforce).GetMethod(nameof(ISalesforce.GetSurfaceAsync))!.ReturnType);

        Assert.DoesNotContain(
            methods,
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)
                && method.Name is not nameof(ISalesforce.GetIdentityAsync)));
        Assert.DoesNotContain(methods, m => m.Name.Contains("Invoke", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, m => m.Name.Contains("Dispatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Read_result_updates_UiSurface_through_outbox_and_feed_event()
    {
        var (core, mcp, _) = CreateCore();
        mcp.QueryResult = new SalesforceQueryResult(5, "five");
        var commandId = Guid.NewGuid();

        var receipt = await core.QueryRecordsAsync(
            new CommandSynapse<SalesforceQueryRequest>(Meta(commandId), new SalesforceQueryRequest("SELECT Id FROM Account")));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(1, mcp.QueryCalls);
        Assert.Equal(1, core.Outbox.Count);
        Assert.Equal(SalesforceFeedEvent.UiSurfaceKind, core.Outbox[0].Event.Payload.Kind);

        await core.DrainOutboxAsync();
        Assert.Equal(0, core.Outbox.Count);

        var surface = core.GetSurface();
        Assert.Equal(SalesforceReactiveCore.SurfaceId, surface.Surface.SurfaceId);
        Assert.Equal("records:5", surface.Surface.Blocks[0].Text);
        Assert.True(surface.Surface.Revision >= 1);
    }

    [Fact]
    public async Task Mutation_intent_is_durable_before_provider_call()
    {
        var (core, mcp, store) = CreateCore();
        var commandId = Guid.NewGuid();
        var fields = new Dictionary<string, string> { ["Name"] = "Acme SECRET_VALUE" };

        var receipt = await core.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId),
                new SalesforceUpdateRequest("Account", "001xx", fields)));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(0, mcp.UpdateCalls);
        Assert.Contains("commit", store.Order);
        Assert.DoesNotContain("provider", store.Order);
        Assert.Equal(1, core.Outbox.Count);
        Assert.Equal(SalesforceFeedEvent.UpdateEffectKind, core.Outbox[0].Event.Payload.Kind);
        Assert.Equal(commandId.ToString("N"), core.Outbox[0].Event.Payload.IdempotencyKey);
        Assert.Equal("update-pending", core.GetSurface().Surface.Blocks[0].Text);

        await core.DrainOutboxAsync();

        Assert.Equal(1, mcp.UpdateCalls);
        Assert.Equal(["commit", "provider", "commit"], store.Order.ToArray());
        Assert.Equal("update-completed", core.GetSurface().Surface.Blocks[0].Text);
        Assert.True(core.Flags.ContainsKey(SalesforceReactiveCore.EffectDoneFlagPrefix + commandId.ToString("N")));
    }

    [Fact]
    public async Task Duplicate_effect_does_not_repeat_provider_mutation()
    {
        var (core, mcp, _) = CreateCore();
        var commandId = Guid.NewGuid();
        await core.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));

        var intent = core.Outbox[0];
        await core.ExecuteOutboxIntentAsync(intent);
        Assert.Equal(1, mcp.UpdateCalls);

        await core.ExecuteOutboxIntentAsync(intent);
        Assert.Equal(1, mcp.UpdateCalls);

        await core.DrainOutboxAsync();
        Assert.Equal(1, mcp.UpdateCalls);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var (core, mcp, _) = CreateCore();
        mcp.UpdateException = new InvalidOperationException("salesforce failed token=abc record=SECRET");
        var commandId = Guid.NewGuid();
        await core.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));

        var ex = await Assert.ThrowsAsync<BrainException>(() => core.DrainOutboxAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);
        Assert.DoesNotContain("token=abc", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", ex.Message, StringComparison.Ordinal);
        Assert.NotEmpty(core.Failures);
        Assert.Equal(BrainErrors.FailureSanitized, core.Failures[^1].Code);
        Assert.Equal(ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage, core.Failures[^1].Message);
    }

    [Fact]
    public async Task Provider_credentials_and_message_bodies_are_absent_from_telemetry()
    {
        var (core, mcp, _) = CreateCore();
        mcp.QueryResult = new SalesforceQueryResult(2, "two");
        var queryId = Guid.NewGuid();
        var updateId = Guid.NewGuid();
        const string fieldValue = "CONFIDENTIAL_RECORD_VALUE";
        const string soql = "SELECT Id, Secret__c FROM Account WHERE Name = 'Acme'";

        await core.QueryRecordsAsync(
            new CommandSynapse<SalesforceQueryRequest>(Meta(queryId), new SalesforceQueryRequest(soql)));
        await core.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(updateId),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = fieldValue })));
        await core.DrainOutboxAsync();

        var blob = string.Join('\n', core.Telemetry);
        Assert.DoesNotContain(fieldValue, blob, StringComparison.Ordinal);
        Assert.DoesNotContain(soql, blob, StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIDENTIAL", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret__c", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("001xx", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("token", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", blob, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OrderingReactiveStore : IReactiveStore<SalesforceFeedEvent>
    {
        private readonly InMemoryReactiveStore<SalesforceFeedEvent> _inner = new();
        public List<string> Order { get; } = [];
        public bool FailNextCommit { get => _inner.FailNextCommit; set => _inner.FailNextCommit = value; }
        public IDictionary<string, CommandReceipt> Receipts => _inner.Receipts;
        public IDictionary<string, byte> ProcessedEvents => _inner.ProcessedEvents;
        public IDictionary<string, long> SourceSequences => _inner.SourceSequences;
        public IList<OutboxIntent<SalesforceFeedEvent>> Outbox => _inner.Outbox;
        public IDictionary<string, string> Domain => _inner.Domain;
        public IDictionary<string, string> Flags => _inner.Flags;
        public IList<SanitizedFailure> Failures => _inner.Failures;
        public IDictionary<string, byte> AcceptedCausation => _inner.AcceptedCausation;
        public IDictionary<string, byte> RejectedCausation => _inner.RejectedCausation;

        public Task CommitAsync()
        {
            Order.Add("commit");
            return _inner.CommitAsync();
        }
    }
}
