extern alias McpProject;

using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.TestSupport;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Hosting;
using BootstrapSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.BootstrapSessionRequest;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using FeedAudienceKind = McpProject::DigitalBrain.V2.Ui.Grpc.FeedAudienceKind;
using LogoutSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.LogoutSessionRequest;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using RefreshSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.RefreshSessionRequest;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using SubmitActionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.SubmitActionRequest;
using UiBootstrapAuthenticator = McpProject::DigitalBrain.Mcp.UiBootstrapAuthenticator;
using UiBootstrapOptions = McpProject::DigitalBrain.Mcp.UiBootstrapOptions;
using UiDeliveryOptions = McpProject::DigitalBrain.Mcp.UiDeliveryOptions;
using UiExternalIdentityAuthenticator = McpProject::DigitalBrain.Mcp.UiExternalIdentityAuthenticator;
using UiExternalIdentityOptions = McpProject::DigitalBrain.Mcp.UiExternalIdentityOptions;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;
using WatchSurfaceFeedRequest = McpProject::DigitalBrain.V2.Ui.Grpc.WatchSurfaceFeedRequest;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiGrpcServiceTests : NeuronTestBase
{
    private const string BootstrapSecret = "task-3-bootstrap-secret";

    protected override void ConfigureSilo(ISiloBuilder builder)
    {
        var keyRing = new RuntimeStateKeyRing(
            1,
            new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)21, 32).ToArray() },
            Enumerable.Repeat((byte)34, 32).ToArray());
        builder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.Conversations)
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.SurfaceFeeds)
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.Sessions)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
                services.AddSingleton(new EncryptedRuntimeStateProtector(keyRing));
            });
    }

    [Fact]
    public async Task V2_interactive_rail_uses_runtime_session_authority_for_session_feed_action_and_logout()
    {
        var (service, sessions) = CreateService();
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Secret = BootstrapSecret },
            TestServerCallContext.WithHeaders(audience));

        var bootstrapped = await sessions.ValidateAccessAsync(bootstrap.AccessToken, SessionAudiences.Ui);
        Assert.NotNull(bootstrapped);
        Assert.Equal(bootstrap.SessionId, bootstrapped.Context.SessionId);

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
            Audience = FeedAudienceKind.Principal,
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

    private (UiGrpcService Service, RuntimeSessionAuthority Sessions) CreateService()
    {
        var timeProvider = TimeProvider.System;
        var tokens = new SessionTokenService(Enumerable.Repeat((byte)13, 32).ToArray(), timeProvider);
        var sessions = new RuntimeSessionAuthority(Cluster.Client, tokens, timeProvider);
        var conversations = new ConversationStateClient(Cluster.Client, timeProvider);
        var service = new UiGrpcService(
            new UiBootstrapAuthenticator(new UiBootstrapOptions(
                BootstrapSecret,
                new TenantId("tenant"),
                new WorkspaceId("workspace"),
                new PrincipalRef("principal", PrincipalKind.User),
                TimeSpan.FromMinutes(15),
                new HashSet<string>(["brain.read", "ui.action"], StringComparer.Ordinal))),
            new UiExternalIdentityAuthenticator(new UiExternalIdentityOptions(
                false,
                string.Empty,
                string.Empty,
                "tenant_id",
                "workspace_id",
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
}
