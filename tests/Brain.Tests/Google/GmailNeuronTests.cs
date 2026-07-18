using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Google;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Google;

public sealed class GmailNeuronTests : IClassFixture<GmailNeuronClusterFixture>
{
    private readonly GmailNeuronClusterFixture _fixture;

    public GmailNeuronTests(GmailNeuronClusterFixture fixture) => _fixture = fixture;

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
        Assert.DoesNotContain(
            methods,
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(string) && method.Name is not nameof(IGmail.GetIdentityAsync)));
    }

    [Fact]
    public void Production_hosting_requires_explicit_mcp_client_and_contains_no_fake()
    {
        Assert.Null(typeof(GmailNeuron).Assembly.GetType("DigitalBrain.Google.FakeGmailMcpClient"));
        Assert.Null(typeof(GmailNeuron).Assembly.GetType("DigitalBrain.Google.GmailReactiveCore"));

        var method = typeof(GmailHosting).GetMethod(nameof(GmailHosting.AddBrainGmail));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(Func<IServiceProvider, IGmailMcpClient>), parameters[1].ParameterType);
        Assert.False(parameters[1].HasDefaultValue);

        Assert.Throws<ArgumentNullException>(() =>
            GmailHosting.AddBrainGmail(null!, _ => new FakeGmailMcpClient()));
    }

    [Fact]
    public void Gmail_agent_uses_typed_MCP_tools()
    {
        var (gmail, _) = Grain("agent-tools");
        var mcp = new FakeGmailMcpClient();
        var tools = GmailMcpTools.CreateTypedTools(mcp, gmail, () => Meta(Guid.NewGuid(), "agent-tools"));
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == GmailMcpTools.ListToolName);
        Assert.Contains(tools, t => t.Name == GmailMcpTools.SendToolName);
        Assert.DoesNotContain(tools, t => t.Name.Contains("invoke", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Gmail_agent_mutating_tool_enters_command_journal_outbox_not_direct_mcp()
    {
        var instance = "agent-mutate";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();

        var sendBefore = _fixture.Mcp.SendCalls;
        var tools = GmailMcpTools.CreateTypedTools(
            _fixture.Mcp,
            gmail,
            () => Meta(Guid.NewGuid(), instance));
        var sendTool = tools.OfType<AIFunction>().Single(t => t.Name == GmailMcpTools.SendToolName);
        var args = new AIFunctionArguments
        {
            ["to"] = "a@example.com",
            ["subject"] = "hi",
            ["body"] = "SECRET_BODY_SHOULD_NOT_HIT_MCP_YET",
        };
        await sendTool.InvokeAsync(args);

        Assert.Equal(sendBefore, _fixture.Mcp.SendCalls);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var head = await control.PeekOutboxAsync();
        Assert.NotNull(head);
        Assert.Equal(GmailFeedEvent.SendEffectKind, head!.Event.Payload.Kind);

        await control.DrainOutboxAsync();
        Assert.Equal(sendBefore + 1, _fixture.Mcp.SendCalls);
    }

    [Fact]
    public async Task Read_result_updates_UiSurface_through_outbox_and_feed_event()
    {
        var instance = "read-ui";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.ListResult = new GmailMessageListResult(3, "three");
        var commandId = Guid.NewGuid();

        var receipt = await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(commandId, instance), new GmailListRequest("is:inbox", 10)));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.True(await control.GetOutboxCountAsync() >= 1);
        Assert.Equal(GmailFeedEvent.UiSurfaceKind, (await control.PeekOutboxAsync())!.Event.Payload.Kind);

        await control.DrainOutboxAsync();
        Assert.Equal(0, await control.GetOutboxCountAsync());

        var surface = await gmail.GetSurfaceAsync();
        Assert.Equal(GmailConstants.SurfaceId, surface.Surface.SurfaceId);
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
    public async Task Mutation_completion_survives_reactivation_with_ui_revision()
    {
        var instance = "mut-reactivate";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));
        var pendingRevision = (await gmail.GetSurfaceAsync()).Surface.Revision;
        await control.DrainOutboxAsync();
        var completed = await gmail.GetSurfaceAsync();
        Assert.Equal("send-completed", completed.Surface.Blocks[0].Text);
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
                var surface = await reloaded.Gmail.GetSurfaceAsync();
                Assert.Equal("send-completed", surface.Surface.Blocks[0].Text);
                Assert.Equal(completed.Surface.Revision, surface.Surface.Revision);
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("gmail mut-reactivate grain did not reactivate");
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
        await control.ReplayOutboxIntentAsync(intent!);
        Assert.Equal(1, _fixture.Mcp.SendCalls);
        await control.DrainOutboxAsync();
        Assert.Equal(1, _fixture.Mcp.SendCalls);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var instance = "fail-provider";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.Reset();
        _fixture.Mcp.SendException = new InvalidOperationException("provider down with token=abc body=secret");
        var commandId = Guid.NewGuid();
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));

        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);
        Assert.DoesNotContain("token=abc", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);

        var failure = await control.GetLastFailureAsync();
        Assert.NotNull(failure);
        Assert.Equal(BrainErrors.FailureSanitized, failure!.Code);
        Assert.Equal(ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage, failure.Message);

        var surface = await gmail.GetSurfaceAsync();
        Assert.Equal("send-failed", surface.Surface.Blocks[0].Text);
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
                Assert.Equal("send-failed", (await reloaded.Gmail.GetSurfaceAsync()).Surface.Blocks[0].Text);
                var durableFailure = await reloaded.Control.GetLastFailureAsync();
                Assert.NotNull(durableFailure);
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("gmail fail-provider grain did not reactivate");
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

        await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(Guid.NewGuid(), instance), new GmailListRequest("from:boss", 5)));
        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(Guid.NewGuid(), instance),
                new GmailSendRequest("a@example.com", "Hello", body)));
        await control.DrainOutboxAsync();

        var blob = string.Join('\n', await control.GetTelemetryAsync());
        Assert.DoesNotContain(body, blob, StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIDENTIAL", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("a@example.com", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("from:boss", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("token", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", blob, StringComparison.OrdinalIgnoreCase);
    }

}
