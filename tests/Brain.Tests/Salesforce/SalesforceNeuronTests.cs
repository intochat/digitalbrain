using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Salesforce;

public sealed class SalesforceNeuronTests : IClassFixture<SalesforceNeuronClusterFixture>
{
    private readonly SalesforceNeuronClusterFixture _fixture;

    public SalesforceNeuronTests(SalesforceNeuronClusterFixture fixture) => _fixture = fixture;

    private static NeuronAddress Address(string instance) =>
        new(new OrganizationId("org-1"), new SpaceId("space-1"), "salesforce.v1", instance);

    private static SynapseMetadata Meta(Guid commandId, string instance) =>
        new(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: new OrganizationId("org-1"),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: new SpaceId("space-1"),
            Source: Address(instance),
            SourceSequence: 1,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

    private (ISalesforce Salesforce, ISalesforceNeuronControl Control) Grain(string instance)
    {
        var key = Address(instance).ToGrainKey();
        return (
            _fixture.Cluster.GrainFactory.GetGrain<ISalesforce>(key),
            _fixture.Cluster.GrainFactory.GetGrain<ISalesforceNeuronControl>(key));
    }

    [Fact]
    public void Salesforce_contract_exposes_only_typed_operations()
    {
        var methods = typeof(ISalesforce).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.QueryRecordsAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.UpdateRecordAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.GetSurfaceAsync));
        Assert.Equal(
            typeof(CommandSynapse<SalesforceQueryRequest>),
            typeof(ISalesforce).GetMethod(nameof(ISalesforce.QueryRecordsAsync))!.GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(CommandSynapse<SalesforceUpdateRequest>),
            typeof(ISalesforce).GetMethod(nameof(ISalesforce.UpdateRecordAsync))!.GetParameters().Single().ParameterType);
        Assert.DoesNotContain(methods, m => m.Name.Contains("Invoke", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            methods,
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(string) && method.Name is not nameof(ISalesforce.GetIdentityAsync)));
    }

    [Fact]
    public void Production_hosting_requires_explicit_mcp_client_and_contains_no_fake()
    {
        Assert.Null(typeof(SalesforceNeuron).Assembly.GetType("DigitalBrain.Salesforce.FakeSalesforceMcpClient"));
        Assert.Null(typeof(SalesforceNeuron).Assembly.GetType("DigitalBrain.Salesforce.SalesforceReactiveCore"));

        var method = typeof(SalesforceHosting).GetMethod(nameof(SalesforceHosting.AddBrainSalesforce));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(Func<IServiceProvider, ISalesforceMcpClient>), parameters[1].ParameterType);
        Assert.False(parameters[1].HasDefaultValue);

        Assert.Throws<ArgumentNullException>(() =>
            SalesforceHosting.AddBrainSalesforce(null!, _ => new FakeSalesforceMcpClient()));
    }

    [Fact]
    public async Task Read_result_updates_UiSurface_through_outbox_and_feed_event()
    {
        var instance = "sf-read-ui";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.QueryResult = new SalesforceQueryResult(5, "five");
        var commandId = Guid.NewGuid();

        var receipt = await salesforce.QueryRecordsAsync(
            new CommandSynapse<SalesforceQueryRequest>(Meta(commandId, instance), new SalesforceQueryRequest("SELECT Id FROM Account")));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        Assert.Equal(SalesforceFeedEvent.UiSurfaceKind, (await control.PeekOutboxAsync())!.Event.Payload.Kind);

        await control.DrainOutboxAsync();
        Assert.Equal(0, await control.GetOutboxCountAsync());

        var surface = await salesforce.GetSurfaceAsync();
        Assert.Equal(SalesforceConstants.SurfaceId, surface.Surface.SurfaceId);
        Assert.Equal("records:5", surface.Surface.Blocks[0].Text);
        Assert.True(surface.Surface.Revision >= 1);
    }

    [Fact]
    public async Task Mutation_intent_is_durable_before_provider_call()
    {
        var instance = "sf-mut-order";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var order = new List<string>();
        _fixture.Mcp.OnUpdate = () => order.Add("provider");
        var commandId = Guid.NewGuid();
        var fields = new Dictionary<string, string> { ["Name"] = "Acme SECRET_VALUE" };

        var receipt = await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", fields)));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(0, _fixture.Mcp.UpdateCalls);
        Assert.DoesNotContain("provider", order);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var head = await control.PeekOutboxAsync();
        Assert.Equal(SalesforceFeedEvent.UpdateEffectKind, head!.Event.Payload.Kind);
        Assert.Equal(commandId.ToString("N"), head.Event.Payload.IdempotencyKey);
        Assert.Equal("update-pending", (await salesforce.GetSurfaceAsync()).Surface.Blocks[0].Text);
        var pendingRevision = (await salesforce.GetSurfaceAsync()).Surface.Revision;

        await control.DrainOutboxAsync();

        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
        Assert.Equal(["provider"], order.ToArray());
        var completed = await salesforce.GetSurfaceAsync();
        Assert.Equal("update-completed", completed.Surface.Blocks[0].Text);
        Assert.True(completed.Surface.Revision > pendingRevision);
    }

    [Fact]
    public async Task Mutation_completion_survives_reactivation_with_ui_revision()
    {
        var instance = "sf-mut-reactivate";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));
        var pendingRevision = (await salesforce.GetSurfaceAsync()).Surface.Revision;
        await control.DrainOutboxAsync();
        var completed = await salesforce.GetSurfaceAsync();
        Assert.Equal("update-completed", completed.Surface.Blocks[0].Text);
        Assert.True(completed.Surface.Revision > pendingRevision);
        var token = await control.GetActivationTokenAsync();
        await control.RequestDeactivationAsync();

        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var reloaded = Grain(instance);
            if (await reloaded.Control.GetActivationTokenAsync() != token)
            {
                var surface = await reloaded.Salesforce.GetSurfaceAsync();
                Assert.Equal("update-completed", surface.Surface.Blocks[0].Text);
                Assert.Equal(completed.Surface.Revision, surface.Surface.Revision);
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("salesforce mut-reactivate grain did not reactivate");
    }

    [Fact]
    public async Task Duplicate_effect_does_not_repeat_provider_mutation()
    {
        var instance = "sf-dup-effect";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));

        var intent = await control.PeekOutboxAsync();
        Assert.NotNull(intent);
        await control.ReplayOutboxIntentAsync(intent!);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
        await control.ReplayOutboxIntentAsync(intent!);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
        await control.DrainOutboxAsync();
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var instance = "sf-fail-provider";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        _fixture.Mcp.UpdateException = new InvalidOperationException("salesforce failed token=abc record=SECRET");
        var commandId = Guid.NewGuid();
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));

        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);
        Assert.DoesNotContain("token=abc", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", ex.Message, StringComparison.Ordinal);

        var failure = await control.GetLastFailureAsync();
        Assert.NotNull(failure);
        Assert.Equal(BrainErrors.FailureSanitized, failure!.Code);

        var surface = await salesforce.GetSurfaceAsync();
        Assert.Equal("update-failed", surface.Surface.Blocks[0].Text);
        Assert.True(surface.Surface.Revision >= 1);

        var token = await control.GetActivationTokenAsync();
        await control.RequestDeactivationAsync();
        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var reloaded = Grain(instance);
            if (await reloaded.Control.GetActivationTokenAsync() != token)
            {
                Assert.Equal("update-failed", (await reloaded.Salesforce.GetSurfaceAsync()).Surface.Blocks[0].Text);
                Assert.NotNull(await reloaded.Control.GetLastFailureAsync());
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("salesforce fail-provider grain did not reactivate");
    }

    [Fact]
    public async Task Provider_credentials_and_message_bodies_are_absent_from_telemetry()
    {
        var instance = "sf-telemetry";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        _fixture.Mcp.QueryResult = new SalesforceQueryResult(2, "two");
        const string fieldValue = "CONFIDENTIAL_RECORD_VALUE";
        const string soql = "SELECT Id, Secret__c FROM Account WHERE Name = 'Acme'";

        await salesforce.QueryRecordsAsync(
            new CommandSynapse<SalesforceQueryRequest>(Meta(Guid.NewGuid(), instance), new SalesforceQueryRequest(soql)));
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(Guid.NewGuid(), instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = fieldValue })));
        await control.DrainOutboxAsync();

        var blob = string.Join('\n', await control.GetTelemetryAsync());
        Assert.DoesNotContain(fieldValue, blob, StringComparison.Ordinal);
        Assert.DoesNotContain(soql, blob, StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIDENTIAL", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret__c", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("001xx", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("token", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", blob, StringComparison.OrdinalIgnoreCase);
    }

}
