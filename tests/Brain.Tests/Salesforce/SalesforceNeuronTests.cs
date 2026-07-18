using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Salesforce;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Salesforce;

public sealed class SalesforceNeuronTests : IClassFixture<SalesforceNeuronClusterFixture>
{
    private readonly SalesforceNeuronClusterFixture _fixture;

    public SalesforceNeuronTests(SalesforceNeuronClusterFixture fixture) => _fixture = fixture;

    private static readonly Guid KnownEffectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExpectedUiSurfaceEventId = Guid.Parse("56587ed9-00c9-a457-765f-f16788af91e1");
    private static readonly Guid ExpectedUpdateCompletedEventId = Guid.Parse("eefe4a84-dde9-ad35-c906-ce15ef471f7c");
    private static readonly Guid ExpectedUpdateFailedEventId = Guid.Parse("f607ccb0-6510-2f11-6e9e-3e73e319bb92");

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

    private async Task<ISalesforceFeedObserver> SubscribeFeedAsync(string instance)
    {
        var streamId = SalesforceConstants.FeedStreamIdFor(Address(instance).ToGrainKey());
        var observer = _fixture.Cluster.GrainFactory.GetGrain<ISalesforceFeedObserver>(streamId);
        await observer.ClearAsync();
        await observer.ReadyAsync(SalesforceConstants.FeedStreamNamespace, streamId);
        return observer;
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
                return;
            await Task.Delay(25);
        }

        throw new TimeoutException("condition not met");
    }

    private async Task<(ISalesforce Salesforce, ISalesforceNeuronControl Control)> ReactivateAsync(
        string instance,
        Guid previousToken)
    {
        await Grain(instance).Control.RequestDeactivationAsync();
        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var reloaded = Grain(instance);
            if (await reloaded.Control.GetActivationTokenAsync() != previousToken)
                return reloaded;
            await Task.Delay(50);
        }

        throw new TimeoutException($"salesforce {instance} did not reactivate");
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ConcurrentBag<Activity> _captured = [];
        private readonly ActivityListener _listener;
        private int _disposed;

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = static _ => true,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                        _captured.Add(activity);
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> SnapshotAndStop()
        {
            Dispose();
            return _captured.ToArray();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _listener.Dispose();
        }
    }

    private static void AssertNoSecrets(IReadOnlyList<Activity> activities, params string[] secrets)
    {
        for (var i = 0; i < activities.Count; i++)
        {
            var activity = activities[i];
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, activity.DisplayName ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, activity.OperationName ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, activity.StatusDescription ?? string.Empty, StringComparison.Ordinal);
                foreach (var tag in activity.Tags)
                {
                    Assert.DoesNotContain(secret, tag.Key, StringComparison.Ordinal);
                    Assert.DoesNotContain(secret, tag.Value ?? string.Empty, StringComparison.Ordinal);
                }

                foreach (var tag in activity.TagObjects)
                {
                    Assert.DoesNotContain(secret, tag.Key, StringComparison.Ordinal);
                    Assert.DoesNotContain(secret, tag.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
                }

                foreach (var baggage in activity.Baggage)
                {
                    Assert.DoesNotContain(secret, baggage.Key, StringComparison.Ordinal);
                    Assert.DoesNotContain(secret, baggage.Value ?? string.Empty, StringComparison.Ordinal);
                }

                foreach (var evt in activity.Events)
                {
                    Assert.DoesNotContain(secret, evt.Name, StringComparison.Ordinal);
                    foreach (var tag in evt.Tags)
                    {
                        Assert.DoesNotContain(secret, tag.Key, StringComparison.Ordinal);
                        Assert.DoesNotContain(secret, tag.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
                    }
                }
            }
        }
    }

    [Fact]
    public void Salesforce_contract_exposes_only_typed_operations()
    {
        var methods = typeof(ISalesforce).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.QueryRecordsAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.UpdateRecordAsync));
        Assert.Contains(methods, m => m.Name == nameof(ISalesforce.GetSurfaceAsync));
        Assert.DoesNotContain(methods, m => m.Name.Contains("Invoke", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_hosting_requires_explicit_mcp_client_and_contains_no_fake()
    {
        Assert.Null(typeof(SalesforceNeuron).Assembly.GetType("DigitalBrain.Salesforce.FakeSalesforceMcpClient"));
        Assert.Null(typeof(SalesforceNeuron).Assembly.GetType("DigitalBrain.Salesforce.SalesforceReactiveCore"));
        var method = typeof(SalesforceHosting).GetMethod(nameof(SalesforceHosting.AddBrainSalesforce));
        Assert.NotNull(method);
        Assert.False(method!.GetParameters()[1].HasDefaultValue);
        Assert.Throws<ArgumentNullException>(() =>
            SalesforceHosting.AddBrainSalesforce(null!, _ => new FakeSalesforceMcpClient()));
    }

    [Fact]
    public void OutcomeEventId_is_cryptographically_deterministic_across_kinds()
    {
        Assert.Equal(ExpectedUiSurfaceEventId, SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UiSurfaceKind));
        Assert.Equal(ExpectedUpdateCompletedEventId, SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UpdateCompletedKind));
        Assert.Equal(ExpectedUpdateFailedEventId, SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UpdateFailedKind));
        Assert.NotEqual(
            SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UpdateCompletedKind),
            SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UpdateFailedKind));
        Assert.Equal(
            SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UpdateCompletedKind),
            SalesforceConstants.OutcomeEventId(KnownEffectId, SalesforceFeedEvent.UpdateCompletedKind));
    }

    [Fact]
    public async Task SurfaceId_is_stable_opaque_per_neuron_identity()
    {
        var (sfA, _) = Grain("surface-a");
        var (sfB, _) = Grain("surface-b");
        var surfaceA = await sfA.GetSurfaceAsync();
        var surfaceB = await sfB.GetSurfaceAsync();
        Assert.Equal(Address("surface-a").ToGrainKey(), surfaceA.Surface.SurfaceId);
        Assert.Equal(Address("surface-b").ToGrainKey(), surfaceB.Surface.SurfaceId);
        Assert.NotEqual(surfaceA.Surface.SurfaceId, surfaceB.Surface.SurfaceId);
        Assert.NotEqual("salesforce.surface", surfaceA.Surface.SurfaceId);
    }

    [Fact]
    public async Task Read_result_updates_UiSurface_through_outbox_and_feed_event()
    {
        var instance = "sf-read-ui";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.QueryResult = new SalesforceQueryResult(5, "five");
        var commandId = Guid.NewGuid();

        var receipt = await salesforce.QueryRecordsAsync(
            new CommandSynapse<SalesforceQueryRequest>(Meta(commandId, instance), new SalesforceQueryRequest("SELECT Id FROM Account")));
        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.True(await control.GetOutboxCountAsync() >= 1);

        await control.DrainOutboxAsync();
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == SalesforceFeedEvent.UiSurfaceKind && e.SurfaceSummary == "records:5"));

        var surface = await salesforce.GetSurfaceAsync();
        Assert.Equal(Address(instance).ToGrainKey(), surface.Surface.SurfaceId);
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

        var receipt = await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme SECRET_VALUE" })));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(0, _fixture.Mcp.UpdateCalls);
        Assert.DoesNotContain("provider", order);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        Assert.Equal(commandId.ToString("N"), (await control.PeekOutboxAsync())!.Event.Payload.IdempotencyKey);
        var pendingRevision = (await salesforce.GetSurfaceAsync()).Surface.Revision;

        await control.DrainOutboxAsync();

        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
        Assert.Equal(["provider"], order.ToArray());
        var completed = await salesforce.GetSurfaceAsync();
        Assert.Equal("update-completed", completed.Surface.Blocks[0].Text);
        Assert.True(completed.Surface.Revision > pendingRevision);
    }

    [Fact]
    public async Task Mutation_result_is_journaled_before_outcome_publish()
    {
        var instance = "sf-journal-before-publish";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        var expectedEventId = SalesforceConstants.OutcomeEventId(commandId, SalesforceFeedEvent.UpdateCompletedKind);

        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));
        await control.SetFailNextOutcomePublishAsync(1);

        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);

        Assert.Equal("update-completed", (await salesforce.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await control.HasEffectTerminalAsync(commandId.ToString("N")));
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var pending = await control.PeekOutboxAsync();
        Assert.Equal(SalesforceFeedEvent.UpdateCompletedKind, pending!.Event.Payload.Kind);
        Assert.Equal(expectedEventId, pending.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        Assert.Equal("update-completed", (await reloaded.Salesforce.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await reloaded.Control.HasEffectTerminalAsync(commandId.ToString("N")));
        Assert.Equal(expectedEventId, (await reloaded.Control.PeekOutboxAsync())!.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);

        await observer.ReadyAsync(
            SalesforceConstants.FeedStreamNamespace,
            SalesforceConstants.FeedStreamIdFor(Address(instance).ToGrainKey()));
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(0, await reloaded.Control.GetOutboxCountAsync());
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == SalesforceFeedEvent.UpdateCompletedKind && e.EffectId == commandId));
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
    }

    [Fact]
    public async Task Mutation_completion_survives_reactivation_with_ui_revision()
    {
        var instance = "sf-mut-reactivate";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));
        var pendingRevision = (await salesforce.GetSurfaceAsync()).Surface.Revision;
        await control.DrainOutboxAsync();
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == SalesforceFeedEvent.UpdateCompletedKind));
        var completed = await salesforce.GetSurfaceAsync();
        Assert.Equal("update-completed", completed.Surface.Blocks[0].Text);
        Assert.True(completed.Surface.Revision > pendingRevision);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        var surface = await reloaded.Salesforce.GetSurfaceAsync();
        Assert.Equal("update-completed", surface.Surface.Blocks[0].Text);
        Assert.Equal(completed.Surface.Revision, surface.Surface.Revision);
        Assert.Equal(Address(instance).ToGrainKey(), surface.Surface.SurfaceId);
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

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        await reloaded.Control.ReplayOutboxIntentAsync(intent!);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var instance = "sf-fail-provider";
        var (salesforce, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.Reset();
        _fixture.Mcp.UpdateException = new InvalidOperationException("salesforce failed token=abc record=SECRET");
        var commandId = Guid.NewGuid();
        var expectedEventId = SalesforceConstants.OutcomeEventId(commandId, SalesforceFeedEvent.UpdateFailedKind);
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(commandId, instance),
                new SalesforceUpdateRequest("Account", "001xx", new Dictionary<string, string> { ["Name"] = "Acme" })));

        await control.SetFailNextOutcomePublishAsync(1);
        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);

        Assert.Equal("update-failed", (await salesforce.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await control.HasEffectTerminalAsync(commandId.ToString("N")));
        Assert.NotNull(await control.GetLastFailureAsync());
        var pending = await control.PeekOutboxAsync();
        Assert.Equal(SalesforceFeedEvent.UpdateFailedKind, pending!.Event.Payload.Kind);
        Assert.Equal(expectedEventId, pending.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        Assert.Equal("update-failed", (await reloaded.Salesforce.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await reloaded.Control.HasEffectTerminalAsync(commandId.ToString("N")));
        Assert.Equal(expectedEventId, (await reloaded.Control.PeekOutboxAsync())!.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);

        await reloaded.Control.ReplayOutboxIntentAsync((await reloaded.Control.PeekOutboxAsync())!);
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);

        await observer.ReadyAsync(
            SalesforceConstants.FeedStreamNamespace,
            SalesforceConstants.FeedStreamIdFor(Address(instance).ToGrainKey()));
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(0, await reloaded.Control.GetOutboxCountAsync());
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == SalesforceFeedEvent.UpdateFailedKind && e.EffectId == commandId));
        Assert.Equal(1, _fixture.Mcp.UpdateCalls);
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
        const string recordId = "001xxSECRET";
        using var capture = new ActivityCapture();

        await salesforce.QueryRecordsAsync(
            new CommandSynapse<SalesforceQueryRequest>(Meta(Guid.NewGuid(), instance), new SalesforceQueryRequest(soql)));
        await salesforce.UpdateRecordAsync(
            new CommandSynapse<SalesforceUpdateRequest>(
                Meta(Guid.NewGuid(), instance),
                new SalesforceUpdateRequest("Account", recordId, new Dictionary<string, string> { ["Name"] = fieldValue })));
        await control.DrainOutboxAsync();

        var snapshot = capture.SnapshotAndStop();
        AssertNoSecrets(snapshot, fieldValue, soql, recordId, "token=abc", "password", "Secret__c");
    }
}
