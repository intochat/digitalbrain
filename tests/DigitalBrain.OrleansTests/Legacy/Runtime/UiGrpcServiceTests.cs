extern alias McpProject;
using System.Security.Claims;
using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.OrleansTests.TestSupport;
using DigitalBrain.Tests.TestSupport;
using Grpc.Core;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Hosting;
using BootstrapSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.BootstrapSessionRequest;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using FeedAudienceKind = McpProject::DigitalBrain.V2.Ui.Grpc.FeedAudienceKind;
using LogoutSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.LogoutSessionRequest;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using RefreshSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.RefreshSessionRequest;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using SubmitActionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.SubmitActionRequest;
using UiDevelopmentLoginAuthenticator = McpProject::DigitalBrain.Mcp.UiDevelopmentLoginAuthenticator;
using UiDevelopmentLoginOptions = McpProject::DigitalBrain.Mcp.UiDevelopmentLoginOptions;
using UiDeliveryOptions = McpProject::DigitalBrain.Mcp.UiDeliveryOptions;
using UiExternalIdentityAuthenticator = McpProject::DigitalBrain.Mcp.UiExternalIdentityAuthenticator;
using UiExternalIdentityOptions = McpProject::DigitalBrain.Mcp.UiExternalIdentityOptions;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;
using WatchSurfaceFeedRequest = McpProject::DigitalBrain.V2.Ui.Grpc.WatchSurfaceFeedRequest;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiGrpcServiceTests : NeuronTestBase
{
    private const string LoginUsername = "admin";
    private const string LoginPassword = "admin";
    private RecordingChatClient? _chatClient;

    protected override void ConfigureSilo(ISiloBuilder builder)
    {
        var keyRing = new RuntimeStateKeyRing(
            1,
            new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)21, 32).ToArray() },
            Enumerable.Repeat((byte)34, 32).ToArray());
        _chatClient = new RecordingChatClient();
        builder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.Conversations)
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.SurfaceFeeds)
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.Sessions)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
                services.AddSingleton(new EncryptedRuntimeStateProtector(keyRing));
                services.AddSingleton<IChatClient>(_chatClient);
                services.AddSingleton<ICapabilityDescriptorSource, GoogleCapabilityDescriptorSource>();
                services.AddSingleton<ICapabilityDescriptorSource, SalesforceCapabilityDescriptorSource>();
                services.AddSingleton<ICapabilityCatalog, BuiltInCapabilityCatalog>();
                services.AddSingleton<ICapabilityResolver, HybridCapabilityResolver>();
                services.AddSingleton<ICapabilityParameterModel, CapabilityParameterModel>();
                services.AddSingleton<IFeatureGrainResolver, OrleansFeatureGrainResolver>();
                services.AddSingleton<IAgentWorkflowRunner, AgentFrameworkWorkflowRunner>();
                services.AddSingleton<IInoEffectExecutor, DisabledInoEffectExecutor>();
            });
    }

    [Fact]
    public async Task V2_interactive_rail_uses_runtime_session_authority_for_session_feed_action_and_logout()
    {
        var (service, sessions) = CreateService();
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));

        var bootstrapped = await sessions.ValidateAccessAsync(bootstrap.AccessToken, SessionAudiences.Ui);
        Assert.NotNull(bootstrapped);
        Assert.Equal(bootstrap.SessionId, bootstrapped.Context.SessionId.Value);
        Assert.Equal("owner", bootstrapped.Context.OwnerId.Value);
        Assert.Equal("principal", bootstrapped.Context.ActorId.Value);
        Assert.Equal(AuthAssurance.Password, bootstrapped.Context.Assurance);
        Assert.Equal(["brain.read", "ui.action"], bootstrapped.Context.Grants.Order(StringComparer.Ordinal).ToArray());
        Assert.InRange(
            bootstrapped.AccessExpiresAt,
            DateTimeOffset.UtcNow.AddMinutes(14),
            DateTimeOffset.UtcNow.AddMinutes(16));

        var refreshed = await service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = bootstrap.RefreshToken },
            TestServerCallContext.WithHeaders(audience));

        Assert.Null(await sessions.ValidateAccessAsync(bootstrap.AccessToken, SessionAudiences.Ui));
        Assert.NotNull(await sessions.ValidateAccessAsync(refreshed.AccessToken, SessionAudiences.Ui));
        Assert.NotEqual(bootstrap.RefreshToken, refreshed.RefreshToken);

        using var feedCancellation = new CancellationTokenSource();
        var writer = new CapturingServerStreamWriter<McpProject::DigitalBrain.V2.Ui.Grpc.SurfaceFeedEvent>(
            feedCancellation.Cancel);
        var watch = new WatchSurfaceFeedRequest
        {
            Audience = FeedAudienceKind.Actor,
            MaxBatchSize = 10
        };
        watch.ClientCapabilities.Add(ConversationSurfacePayload.RequiredCapabilities);
        await service.WatchSurfaceFeed(
            watch,
            writer,
            TestServerCallContext.WithHeaders(
                feedCancellation.Token,
                ("x-v2-session", refreshed.AccessToken),
                audience));

        var feedEvent = Assert.Single(writer.Messages);
        Assert.Equal(McpProject::DigitalBrain.V2.Ui.Grpc.SurfaceFeedEvent.EventOneofCase.SurfaceJson, feedEvent.EventCase);
        using var surface = JsonDocument.Parse(feedEvent.SurfaceJson);
        var action = Assert.Single(surface.RootElement.GetProperty("actions").EnumerateArray());

        var accepted = await service.SubmitAction(
            new SubmitActionRequest
            {
                BindingId = action.GetProperty("bindingId").GetString(),
                ActionToken = action.GetProperty("actionToken").GetString(),
                SurfaceId = action.GetProperty("surfaceId").GetString(),
                SurfaceRevision = action.GetProperty("surfaceRevision").GetInt32(),
                InputJson = """{"prompt":"Retained V2 action"}"""
            },
            TestServerCallContext.WithHeaders(("x-v2-session", refreshed.AccessToken), audience));

        Assert.StartsWith("runtime-op-", accepted.OperationId, StringComparison.Ordinal);
        Assert.NotEmpty(accepted.IdempotencyKey);

        await service.LogoutSession(
            new LogoutSessionRequest { RefreshToken = refreshed.RefreshToken },
            TestServerCallContext.WithHeaders(audience));

        Assert.Null(await sessions.ValidateAccessAsync(refreshed.AccessToken, SessionAudiences.Ui));
        Assert.Null(await sessions.RefreshAsync(
            refreshed.RefreshToken,
            TimeSpan.FromMinutes(15),
            SessionAudiences.Ui));
    }

    [Fact]
    public async Task V2_refresh_replay_is_rejected_and_revokes_the_rotated_session()
    {
        var (service, sessions) = CreateService();
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var refreshed = await service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = bootstrap.RefreshToken },
            TestServerCallContext.WithHeaders(audience));

        var replay = await Assert.ThrowsAsync<RpcException>(() => service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = bootstrap.RefreshToken },
            TestServerCallContext.WithHeaders(audience)));

        Assert.Equal(StatusCode.Unauthenticated, replay.StatusCode);
        Assert.Null(await sessions.ValidateAccessAsync(refreshed.AccessToken, SessionAudiences.Ui));
        var rotatedRefresh = await Assert.ThrowsAsync<RpcException>(() => service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = refreshed.RefreshToken },
            TestServerCallContext.WithHeaders(audience)));
        Assert.Equal(StatusCode.Unauthenticated, rotatedRefresh.StatusCode);
    }

    [Fact]
    public async Task V2_invalid_development_credentials_fail_with_the_same_safe_status()
    {
        var (service, _) = CreateService();
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var pairs = new[]
        {
            ("wrong", LoginPassword),
            (LoginUsername, "wrong"),
            (string.Empty, LoginPassword),
            (LoginUsername, string.Empty),
            (new string('a', 257), LoginPassword),
            (LoginUsername, new string('a', 257))
        };

        foreach (var (username, password) in pairs)
        {
            var exception = await Assert.ThrowsAsync<RpcException>(() => service.BootstrapSession(
                new BootstrapSessionRequest { Username = username, Password = password },
                TestServerCallContext.WithHeaders(audience)));

            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
            Assert.Equal(
                "A valid UI session for the exact transport audience is required.",
                exception.Status.Detail);
        }
    }

    [Fact]
    public async Task V2_oidc_login_accepts_an_empty_credential_body()
    {
        var externalOptions = new UiExternalIdentityOptions(
            true,
            "https://issuer.example/tenant",
            "digitalbrain-ui",
            "sub",
            "digitalbrain_grants",
            new HashSet<string>(["brain.read", "ui.action"], StringComparer.Ordinal),
            true);
        var (service, sessions) = CreateService(externalOptions);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "subject"),
                new Claim("digitalbrain_grants", "brain.read ui.action")
            ],
            "oidc"));
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new FixedAuthenticationService(principal))
            .BuildServiceProvider();
        var call = TestServerCallContext.WithHeaders(
            ("x-v2-audience", SessionAudiences.Ui));
        var httpContext = call.GetHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Headers.Authorization = "Bearer header.payload.signature";

        var reply = await service.BootstrapSession(new BootstrapSessionRequest(), call);
        var validated = await sessions.ValidateAccessAsync(reply.AccessToken, SessionAudiences.Ui);

        Assert.NotNull(validated);
        Assert.Equal(AuthAssurance.Oidc, validated.Context.Assurance);
        Assert.Equal(["brain.read", "ui.action"], validated.Context.Grants.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Wrong_revision_is_mapped_to_failed_precondition()
    {
        Assert.Equal(
            StatusCode.FailedPrecondition,
            UiGrpcService.StatusForActionRejection(ActionRejection.WrongRevision));
    }

    [Fact]
    public void Stale_unavailable_action_is_mapped_to_failed_precondition()
    {
        Assert.Equal(
            StatusCode.FailedPrecondition,
            UiGrpcService.StatusForActionRejection(ActionRejection.Unavailable));
    }

    [Fact]
    public void Forged_action_is_mapped_to_permission_denied()
    {
        Assert.Equal(
            StatusCode.PermissionDenied,
            UiGrpcService.StatusForActionRejection(ActionRejection.Forged));
    }

    [Fact]
    public void Action_tokens_are_refreshed_only_when_the_binding_set_changes()
    {
        var issuedBinding = new SurfaceActionBinding(
            ConversationSurfacePayload.SendBindingId,
            ConversationSurfacePayload.HomeSurfaceId,
            1,
            ConversationSurfacePayload.SendActionType,
            ConversationSurfacePayload.SendInputSchema,
            "ui.action",
            UiProtocol.ActionSchemaVersion,
            new string('a', 64),
            1,
            0,
            DateTimeOffset.UtcNow.AddMinutes(5),
            null,
            null);

        Assert.False(UiGrpcService.ActionBindingsChanged([issuedBinding], [issuedBinding]));
        Assert.True(UiGrpcService.ActionBindingsChanged(
            [issuedBinding],
            [issuedBinding with { SurfaceRevision = 2, TokenHash = new string('b', 64) }]));
        Assert.True(UiGrpcService.ActionBindingsChanged([issuedBinding], []));
    }

    [Fact]
    public async Task Missing_capability_request_creates_one_durable_feature_draft_and_never_calls_the_general_chat_model()
    {
        const string Prompt = "Research Acme Corporation and create a text file with the findings.";
        var context = new RuntimeRequestContext(
            new BrainOwnerId("owner-capability-proposal"),
            new ActorId("principal-capability-proposal"),
            new SessionId("session-capability-proposal"),
            AuthAssurance.Oidc,
            "request-capability-proposal",
            null,
            new HashSet<string>(StringComparer.Ordinal));
        var composedCatalog = Cluster.Silos.Single().ServiceProvider
            .GetRequiredService<ICapabilityCatalog>()
            .Snapshot();
        Assert.Equal(8, composedCatalog.Count);
        Assert.Contains(composedCatalog, descriptor => descriptor.Id == GoogleCapabilityIds.GmailMessageRead);
        Assert.Contains(composedCatalog, descriptor => descriptor.Id == SalesforceCapabilityIds.RecordRead);
        var conversations = new ConversationStateClient(Cluster.Client, TimeProvider.System);
        var handler = new McpInoCommandHandler(conversations);
        var command = new CommandEnvelope(
            McpInoCommandHandler.CommandType,
            1,
            "command-capability-proposal",
            context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt }));

        var accepted = await handler.AcceptAsync(command);
        var conversation = Grain<IConversationNeuron>(RuntimeStateKeys.Conversation(
            context.OwnerId,
            context.ActorId,
            InoConversationIdentity.From(context)));
        var completed = await WaitForOperationAsync(
            conversation,
            accepted.OperationId,
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));

        Assert.Equal(CapabilityResolutionKind.Missing, completed.Capability?.Kind);
        Assert.NotNull(completed.Proposal);
        Assert.StartsWith("proposal-", completed.Proposal!.ProposalId, StringComparison.Ordinal);
        Assert.Equal("Open Studio", completed.Proposal.Label);
        var state = await conversation.ReadAsync();
        var assistantTurn = state.Turns.Single(turn =>
            turn.OperationId == accepted.OperationId && turn.Kind == ConversationTurnKind.Assistant);
        Assert.Contains("I don’t have a trusted capability", assistantTurn.Text, StringComparison.Ordinal);
        Assert.Equal(0, _chatClient!.CallCount);

        var replayAccepted = await handler.AcceptAsync(command);
        Assert.Equal(accepted.OperationId, replayAccepted.OperationId);
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var replayRequestedAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var replayDraft = await hub.CreateDraftAsync(new CreateFeatureDraft(accepted.OperationId, Prompt, replayRequestedAt));
        Assert.Equal(completed.Proposal.ProposalId, replayDraft.ProposalId);
        Assert.NotEqual(replayRequestedAt, replayDraft.CreatedAt);
        Assert.Equal(0, _chatClient!.CallCount);

        var refreshedSnapshot = await conversations.ReadAsync(context);
        var payload = ConversationSurfacePayload.Build(refreshedSnapshot);
        var operationJson = payload.GetProperty("data").GetProperty("operation");
        Assert.Equal("missing", operationJson.GetProperty("capability").GetProperty("kind").GetString());
        Assert.Equal(completed.Proposal.ProposalId, operationJson.GetProperty("proposal").GetProperty("id").GetString());
        Assert.Equal(completed.Proposal.Route, operationJson.GetProperty("proposal").GetProperty("route").GetString());
    }

    private static async Task<ConversationOperation> WaitForOperationAsync(
        IConversationNeuron conversation,
        string operationId,
        ConversationOperationStatus expectedStatus,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var operation = (await conversation.ReadAsync()).Operations.Single(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
            if (operation.Status == expectedStatus) return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        var final = (await conversation.ReadAsync()).Operations.Single(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        throw new Xunit.Sdk.XunitException(
            $"Operation {operationId} did not reach {expectedStatus}; final state was {final.Status}.");
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "safe response"))
            {
                ConversationId = "provider-conversation"
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private (UiGrpcService Service, RuntimeSessionAuthority Sessions) CreateService(
        UiExternalIdentityOptions? externalOptions = null)
    {
        var timeProvider = TimeProvider.System;
        var tokens = new SessionTokenService(Enumerable.Repeat((byte)13, 32).ToArray(), timeProvider);
        var sessions = new RuntimeSessionAuthority(Cluster.Client, tokens, timeProvider);
        var conversations = new ConversationStateClient(Cluster.Client, timeProvider);
        var service = new UiGrpcService(
            new UiDevelopmentLoginAuthenticator(new UiDevelopmentLoginOptions(
                LoginUsername,
                LoginPassword,
                new BrainOwnerId("owner"),
                new ActorId("principal"),
                TimeSpan.FromMinutes(15),
                new HashSet<string>(["brain.read", "ui.action"], StringComparer.Ordinal))),
            new UiExternalIdentityAuthenticator(externalOptions ?? new UiExternalIdentityOptions(
                false,
                string.Empty,
                string.Empty,
                "sub",
                "digitalbrain_grants",
                new HashSet<string>(StringComparer.Ordinal),
                true)),
            sessions,
            new RuntimeSurfaceFeed(Cluster.Client, timeProvider, tokens),
            new SurfaceEnvelopeWriter(),
            new McpInoCommandHandler(conversations),
            conversations,
            UiDeliveryOptions.Default,
            NullLogger<UiGrpcService>.Instance);
        return (service, sessions);
    }

    private sealed class FixedAuthenticationService(ClaimsPrincipal principal) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme!)));

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
