using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Google;
using Microsoft.Extensions.AI;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Google;

public sealed class GmailNeuronTests : IClassFixture<GmailNeuronClusterFixture>
{
    private readonly GmailNeuronClusterFixture _fixture;

    public GmailNeuronTests(GmailNeuronClusterFixture fixture) => _fixture = fixture;

    private static readonly Guid KnownEffectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExpectedUiSurfaceEventId = Guid.Parse("56587ed9-00c9-a457-765f-f16788af91e1");
    private static readonly Guid ExpectedSendCompletedEventId = Guid.Parse("38033696-6732-1561-a158-f13c36206684");
    private static readonly Guid ExpectedSendFailedEventId = Guid.Parse("70836d5b-349a-46bb-2ebf-07e97d535e71");

    private static NeuronAddress Address(string instance) =>
        new(new OrganizationId("org-1"), new SpaceId("space-1"), "google.gmail.v1", instance);

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

    private (IGmail Gmail, IGmailNeuronControl Control) Grain(string instance)
    {
        var key = Address(instance).ToGrainKey();
        return (
            _fixture.Cluster.GrainFactory.GetGrain<IGmail>(key),
            _fixture.Cluster.GrainFactory.GetGrain<IGmailNeuronControl>(key));
    }

    private async Task<IGmailFeedObserver> SubscribeFeedAsync(string instance)
    {
        var streamId = GmailConstants.FeedStreamIdFor(Address(instance).ToGrainKey());
        var observer = _fixture.Cluster.GrainFactory.GetGrain<IGmailFeedObserver>(streamId);
        await observer.ClearAsync();
        await observer.ReadyAsync(GmailConstants.FeedStreamNamespace, streamId);
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

    private async Task<(IGmail Gmail, IGmailNeuronControl Control)> ReactivateAsync(string instance, Guid previousToken)
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

        throw new TimeoutException($"gmail {instance} did not reactivate");
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
    public void Gmail_contract_exposes_only_typed_operations()
    {
        var methods = typeof(IGmail).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(methods, m => m.Name == nameof(IGmail.ListMessagesAsync));
        Assert.Contains(methods, m => m.Name == nameof(IGmail.SendMessageAsync));
        Assert.Contains(methods, m => m.Name == nameof(IGmail.GetSurfaceAsync));
        Assert.Equal(
            typeof(CommandSynapse<GmailListRequest>),
            typeof(IGmail).GetMethod(nameof(IGmail.ListMessagesAsync))!.GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(CommandSynapse<GmailSendRequest>),
            typeof(IGmail).GetMethod(nameof(IGmail.SendMessageAsync))!.GetParameters().Single().ParameterType);
        Assert.DoesNotContain(methods, m => m.Name.Contains("Invoke", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_hosting_requires_explicit_mcp_client_and_contains_no_fake()
    {
        Assert.Null(typeof(GmailNeuron).Assembly.GetType("DigitalBrain.Google.FakeGmailMcpClient"));
        Assert.Null(typeof(GmailNeuron).Assembly.GetType("DigitalBrain.Google.GmailReactiveCore"));
        var method = typeof(GmailHosting).GetMethod(nameof(GmailHosting.AddBrainGmail));
        Assert.NotNull(method);
        Assert.False(method!.GetParameters()[1].HasDefaultValue);
        Assert.Throws<ArgumentNullException>(() =>
            GmailHosting.AddBrainGmail(null!, _ => new FakeGmailMcpClient()));
    }

    [Fact]
    public void OutcomeEventId_is_cryptographically_deterministic_across_kinds()
    {
        Assert.Equal(ExpectedUiSurfaceEventId, GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.UiSurfaceKind));
        Assert.Equal(ExpectedSendCompletedEventId, GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.SendCompletedKind));
        Assert.Equal(ExpectedSendFailedEventId, GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.SendFailedKind));
        Assert.NotEqual(
            GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.SendCompletedKind),
            GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.SendFailedKind));
        Assert.Equal(
            GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.SendCompletedKind),
            GmailConstants.OutcomeEventId(KnownEffectId, GmailFeedEvent.SendCompletedKind));
    }

    [Fact]
    public void Gmail_agent_uses_typed_MCP_tools()
    {
        var (gmail, _) = Grain("agent-tools");
        var tools = GmailMcpTools.CreateTypedTools(new FakeGmailMcpClient(), gmail, () => Meta(Guid.NewGuid(), "agent-tools"));
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == GmailMcpTools.ListToolName);
        Assert.Contains(tools, t => t.Name == GmailMcpTools.SendToolName);
    }

    [Fact]
    public async Task SurfaceId_is_stable_opaque_per_neuron_identity()
    {
        var (gmailA, _) = Grain("surface-a");
        var (gmailB, _) = Grain("surface-b");
        var surfaceA = await gmailA.GetSurfaceAsync();
        var surfaceB = await gmailB.GetSurfaceAsync();
        Assert.Equal(Address("surface-a").ToGrainKey(), surfaceA.Surface.SurfaceId);
        Assert.Equal(Address("surface-b").ToGrainKey(), surfaceB.Surface.SurfaceId);
        Assert.NotEqual(surfaceA.Surface.SurfaceId, surfaceB.Surface.SurfaceId);
        Assert.NotEqual("gmail.surface", surfaceA.Surface.SurfaceId);
    }

    [Fact]
    public async Task Gmail_agent_mutating_tool_enters_command_journal_outbox_not_direct_mcp()
    {
        var instance = "agent-mutate";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var sendBefore = _fixture.Mcp.SendCalls;
        var tools = GmailMcpTools.CreateTypedTools(_fixture.Mcp, gmail, () => Meta(Guid.NewGuid(), instance));
        var sendTool = tools.OfType<AIFunction>().Single(t => t.Name == GmailMcpTools.SendToolName);
        await sendTool.InvokeAsync(new AIFunctionArguments
        {
            ["to"] = "a@example.com",
            ["subject"] = "hi",
            ["body"] = "SECRET_BODY_SHOULD_NOT_HIT_MCP_YET",
        });
        Assert.Equal(sendBefore, _fixture.Mcp.SendCalls);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        Assert.Equal(GmailFeedEvent.SendEffectKind, (await control.PeekOutboxAsync())!.Event.Payload.Kind);
        await control.DrainOutboxAsync();
        Assert.Equal(sendBefore + 1, _fixture.Mcp.SendCalls);
    }

    [Fact]
    public async Task Read_result_updates_UiSurface_through_outbox_and_feed_event()
    {
        var instance = "read-ui";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.ListResult = new GmailMessageListResult(3, "three");
        var commandId = Guid.NewGuid();

        var receipt = await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(commandId, instance), new GmailListRequest("is:inbox", 10)));
        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        Assert.Equal(GmailFeedEvent.UiSurfaceKind, (await control.PeekOutboxAsync())!.Event.Payload.Kind);

        await control.DrainOutboxAsync();
        Assert.Equal(0, await control.GetOutboxCountAsync());

        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.UiSurfaceKind && e.SurfaceSummary == "messages:3"));

        var surface = await gmail.GetSurfaceAsync();
        Assert.Equal(Address(instance).ToGrainKey(), surface.Surface.SurfaceId);
        Assert.Equal("messages:3", surface.Surface.Blocks[0].Text);
        Assert.True(surface.Surface.Revision >= 1);
    }

    [Fact]
    public async Task Mutation_intent_is_durable_before_provider_call()
    {
        var instance = "mut-order";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var order = new List<string>();
        _fixture.Mcp.OnSend = () => order.Add("provider");
        var commandId = Guid.NewGuid();

        var receipt = await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "SECRET_BODY")));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(0, _fixture.Mcp.SendCalls);
        Assert.DoesNotContain("provider", order);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var head = await control.PeekOutboxAsync();
        Assert.Equal(GmailFeedEvent.SendEffectKind, head!.Event.Payload.Kind);
        Assert.Equal(commandId.ToString("N"), head.Event.Payload.IdempotencyKey);
        Assert.Equal("send-pending", (await gmail.GetSurfaceAsync()).Surface.Blocks[0].Text);
        var pendingRevision = (await gmail.GetSurfaceAsync()).Surface.Revision;

        await control.DrainOutboxAsync();

        Assert.Equal(1, _fixture.Mcp.SendCalls);
        Assert.Equal(["provider"], order.ToArray());
        var completed = await gmail.GetSurfaceAsync();
        Assert.Equal("send-completed", completed.Surface.Blocks[0].Text);
        Assert.True(completed.Surface.Revision > pendingRevision);
    }

    [Fact]
    public async Task Mutation_result_is_journaled_before_outcome_publish()
    {
        var instance = "journal-before-publish";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        var expectedEventId = GmailConstants.OutcomeEventId(commandId, GmailFeedEvent.SendCompletedKind);

        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));
        await control.SetFailNextOutcomePublishAsync(1);

        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);

        Assert.Equal("send-completed", (await gmail.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await control.HasEffectTerminalAsync(commandId.ToString("N")));
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var pending = await control.PeekOutboxAsync();
        Assert.NotNull(pending);
        Assert.Equal(GmailFeedEvent.SendCompletedKind, pending!.Event.Payload.Kind);
        Assert.Equal(expectedEventId, pending.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.SendCalls);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        Assert.Equal("send-completed", (await reloaded.Gmail.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await reloaded.Control.HasEffectTerminalAsync(commandId.ToString("N")));
        var pendingAfter = await reloaded.Control.PeekOutboxAsync();
        Assert.NotNull(pendingAfter);
        Assert.Equal(expectedEventId, pendingAfter!.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.SendCalls);

        await observer.ReadyAsync(GmailConstants.FeedStreamNamespace, GmailConstants.FeedStreamIdFor(Address(instance).ToGrainKey()));
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(0, await reloaded.Control.GetOutboxCountAsync());
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.SendCompletedKind && e.EffectId == commandId));
        Assert.Equal(1, _fixture.Mcp.SendCalls);
    }

    [Fact]
    public async Task Mutation_completion_survives_reactivation_with_ui_revision()
    {
        var instance = "mut-reactivate";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));
        var pendingRevision = (await gmail.GetSurfaceAsync()).Surface.Revision;
        await control.DrainOutboxAsync();
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.SendCompletedKind));
        var completed = await gmail.GetSurfaceAsync();
        Assert.Equal("send-completed", completed.Surface.Blocks[0].Text);
        Assert.True(completed.Surface.Revision > pendingRevision);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        var surface = await reloaded.Gmail.GetSurfaceAsync();
        Assert.Equal("send-completed", surface.Surface.Blocks[0].Text);
        Assert.Equal(completed.Surface.Revision, surface.Surface.Revision);
        Assert.Equal(Address(instance).ToGrainKey(), surface.Surface.SurfaceId);
    }

    [Fact]
    public async Task Duplicate_effect_does_not_repeat_provider_mutation()
    {
        var instance = "dup-effect";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));

        var intent = await control.PeekOutboxAsync();
        Assert.NotNull(intent);
        await control.ReplayOutboxIntentAsync(intent!);
        Assert.Equal(1, _fixture.Mcp.SendCalls);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        await reloaded.Control.ReplayOutboxIntentAsync(intent!);
        Assert.Equal(1, _fixture.Mcp.SendCalls);
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(1, _fixture.Mcp.SendCalls);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var instance = "fail-provider";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var observer = await SubscribeFeedAsync(instance);
        _fixture.Mcp.Reset();
        _fixture.Mcp.SendException = new InvalidOperationException("provider down with token=abc body=secret");
        var commandId = Guid.NewGuid();
        var expectedEventId = GmailConstants.OutcomeEventId(commandId, GmailFeedEvent.SendFailedKind);
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));

        await control.SetFailNextOutcomePublishAsync(1);
        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);

        Assert.Equal("send-failed", (await gmail.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await control.HasEffectTerminalAsync(commandId.ToString("N")));
        var failure = await control.GetLastFailureAsync();
        Assert.NotNull(failure);
        Assert.Equal(BrainErrors.FailureSanitized, failure!.Code);
        Assert.DoesNotContain("token=abc", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", failure.Message, StringComparison.Ordinal);

        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var pending = await control.PeekOutboxAsync();
        Assert.NotNull(pending);
        Assert.Equal(GmailFeedEvent.SendFailedKind, pending!.Event.Payload.Kind);
        Assert.Equal(expectedEventId, pending.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.SendCalls);

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        Assert.Equal("send-failed", (await reloaded.Gmail.GetSurfaceAsync()).Surface.Blocks[0].Text);
        Assert.True(await reloaded.Control.HasEffectTerminalAsync(commandId.ToString("N")));
        Assert.NotNull(await reloaded.Control.GetLastFailureAsync());
        var pendingAfter = await reloaded.Control.PeekOutboxAsync();
        Assert.Equal(expectedEventId, pendingAfter!.Event.Metadata.EventId);
        Assert.Equal(1, _fixture.Mcp.SendCalls);

        await reloaded.Control.ReplayOutboxIntentAsync(pendingAfter);
        Assert.Equal(1, _fixture.Mcp.SendCalls);

        await observer.ReadyAsync(GmailConstants.FeedStreamNamespace, GmailConstants.FeedStreamIdFor(Address(instance).ToGrainKey()));
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(0, await reloaded.Control.GetOutboxCountAsync());
        await WaitForAsync(async () =>
            (await observer.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.SendFailedKind && e.EffectId == commandId));
        Assert.Equal(1, _fixture.Mcp.SendCalls);
    }

    [Fact]
    public async Task Provider_credentials_and_message_bodies_are_absent_from_telemetry()
    {
        var instance = "telemetry";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        _fixture.Mcp.ListResult = new GmailMessageListResult(1, "one");
        const string body = "CONFIDENTIAL_MESSAGE_BODY";
        const string recipient = "a@example.com";
        const string subject = "HelloSecretSubject";
        const string query = "from:boss SECRET_QUERY";
        using var capture = new ActivityCapture();

        await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(Guid.NewGuid(), instance), new GmailListRequest(query, 5)));
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(Guid.NewGuid(), instance),
                new GmailSendRequest(recipient, subject, body)));
        await control.DrainOutboxAsync();

        var snapshot = capture.SnapshotAndStop();
        AssertNoSecrets(snapshot, body, recipient, subject, query, "token=abc", "oauth", "password");
    }
}
