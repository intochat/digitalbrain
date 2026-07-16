extern alias McpProject;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.OrleansTests.TestSupport;
using DigitalBrain.OrleansTests.Features;
using DigitalBrain.Tests.TestSupport;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Hosting;
using BootstrapSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.BootstrapSessionRequest;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using DigitalBrainQueryService = McpProject::DigitalBrain.Mcp.DigitalBrainQueryService;
using DigitalBrainUiEndpoints = McpProject::DigitalBrain.Mcp.DigitalBrainUiEndpoints;
using FeedAudienceKind = McpProject::DigitalBrain.V2.Ui.Grpc.FeedAudienceKind;
using FeatureArtifactCatalog = McpProject::DigitalBrain.Mcp.IFeatureArtifactCatalog;
using FeatureAuthoringService = McpProject::DigitalBrain.Mcp.FeatureAuthoringService;
using FeatureBuildArtifact = McpProject::DigitalBrain.Mcp.FeatureBuildArtifact;
using FeatureBuildEndpoint = McpProject::DigitalBrain.Mcp.IFeatureBuildEndpoint;
using FeatureBuildSubmission = McpProject::DigitalBrain.Mcp.FeatureBuildSubmission;
using FeatureCapabilityCatalog = McpProject::DigitalBrain.Mcp.IFeatureCapabilityCatalog;
using FeatureInstallationInspection = McpProject::DigitalBrain.Mcp.FeatureInstallationInspection;
using FeatureLifecycleInspection = McpProject::DigitalBrain.Mcp.FeatureLifecycleInspection;
using FeatureLifecycleRail = McpProject::DigitalBrain.Mcp.IFeatureLifecycleRail;
using FeatureRunInstallationInspection = McpProject::DigitalBrain.Mcp.FeatureRunInstallationInspection;
using FeatureRunLifecycleInspection = McpProject::DigitalBrain.Mcp.FeatureRunLifecycleInspection;
using FeatureSuggestionService = McpProject::DigitalBrain.Mcp.FeatureSuggestionService;
using GetRunRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetRunRequest;
using GetConversationContextRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetConversationContextRequest;
using GetFeatureRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureRequest;
using GetFeatureReleaseSourceRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureReleaseSourceRequest;
using GetFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureDraftRequest;
using GrpcFeatureBehavior = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureBehavior;
using GrpcFeatureDraft = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraft;
using GrpcFeatureGrant = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureGrant;
using GrpcFeatureScenario = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureScenario;
using InstallFeatureVersionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.InstallFeatureVersionRequest;
using ListActivityRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ListActivityRequest;
using ListFeaturesRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ListFeaturesRequest;
using LogoutSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.LogoutSessionRequest;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using RefreshSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.RefreshSessionRequest;
using ResetFeatureDraftInstallationRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ResetFeatureDraftInstallationRequest;
using ResumeOriginatingRequestRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ResumeOriginatingRequestRequest;
using ReviewFeatureAccessRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviewFeatureAccessRequest;
using ReviseFeatureBehaviorInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureBehaviorInput;
using ReviseFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureDraftRequest;
using RollbackFeatureVersionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.RollbackFeatureVersionRequest;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using SubmitActionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.SubmitActionRequest;
using SuggestFeatureChangeRequest = McpProject::DigitalBrain.V2.Ui.Grpc.SuggestFeatureChangeRequest;
using UiDeliveryOptions = McpProject::DigitalBrain.Mcp.UiDeliveryOptions;
using UiDevelopmentLoginAuthenticator = McpProject::DigitalBrain.Mcp.UiDevelopmentLoginAuthenticator;
using UiDevelopmentLoginOptions = McpProject::DigitalBrain.Mcp.UiDevelopmentLoginOptions;
using UiExternalIdentityAuthenticator = McpProject::DigitalBrain.Mcp.UiExternalIdentityAuthenticator;
using UiExternalIdentityOptions = McpProject::DigitalBrain.Mcp.UiExternalIdentityOptions;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;
using VerifyFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.VerifyFeatureDraftRequest;
using WatchSurfaceFeedRequest = McpProject::DigitalBrain.V2.Ui.Grpc.WatchSurfaceFeedRequest;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiGrpcServiceTests : NeuronTestBase
{
    private const string LoginUsername = "admin";
    private const string LoginPassword = "admin";
    private RecordingChatClient? _chatClient;
    private readonly TestFeaturePublicationVerifier _publicationVerifier = new();

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
                services.AddSingleton<IFeaturePublicationVerifier>(_publicationVerifier);
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
    public async Task Product_authoring_methods_share_authentication_and_Feature_authority_checks()
    {
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var (service, _) = CreateService();
        var unauthenticatedCalls = ProductCalls(service, TestServerCallContext.WithHeaders(audience));

        foreach (var call in unauthenticatedCalls)
        {
            var exception = await Assert.ThrowsAsync<RpcException>(call);
            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
            Assert.Equal(
                "A valid UI session for the exact transport audience is required.",
                exception.Status.Detail);
        }

        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var deniedContext = TestServerCallContext.WithHeaders(
            ("x-v2-session", bootstrap.AccessToken),
            audience);

        foreach (var call in ProductCalls(service, deniedContext))
        {
            var exception = await Assert.ThrowsAsync<RpcException>(call);
            Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
            Assert.Equal("Feature management authority is required.", exception.Status.Detail);
        }
    }

    [Fact]
    public async Task Activity_methods_require_a_UI_session_and_brain_read_authority()
    {
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var (service, _) = CreateService(grants: new HashSet<string>(StringComparer.Ordinal));

        foreach (var call in ActivityCalls(service, TestServerCallContext.WithHeaders(audience)))
        {
            var exception = await Assert.ThrowsAsync<RpcException>(call);
            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
            Assert.Equal(
                "A valid UI session for the exact transport audience is required.",
                exception.Status.Detail);
        }

        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var deniedContext = TestServerCallContext.WithHeaders(
            ("x-v2-session", bootstrap.AccessToken),
            audience);

        foreach (var call in ActivityCalls(service, deniedContext))
        {
            var exception = await Assert.ThrowsAsync<RpcException>(call);
            Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
            Assert.Equal("Activity read authority is required.", exception.Status.Detail);
        }
    }

    [Fact]
    public async Task Activity_list_and_detail_apply_typed_filters_and_project_only_safe_run_metadata()
    {
        var occurredAt = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var installationId = new FeatureInstallationId("installation-ui-activity");
        var release = new ReleaseDigest(new string('a', 64));
        var matching = ActivityRun(
            "run-ui-activity-event",
            installationId,
            release,
            FeatureRunOrigin.Event,
            FeatureRunStatus.Failed,
            FeatureRunAuthorityState.Paused,
            occurredAt,
            2);
        var other = ActivityRun(
            "run-ui-activity-chat",
            installationId,
            release,
            FeatureRunOrigin.Chat,
            FeatureRunStatus.Completed,
            FeatureRunAuthorityState.Authorized,
            occurredAt.AddMinutes(-5),
            1) with
        {
            CompletedAt = occurredAt.AddMinutes(-4)
        };
        var queries = ActivityQueries(installationId, release, matching, other);
        var (service, _) = CreateService(
            grants: new HashSet<string>(["brain.read"], StringComparer.Ordinal),
            queries: queries);
        var call = await AuthenticatedCallAsync(service);

        var list = await service.ListActivity(
            new ListActivityRequest
            {
                Status = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunStatus.Failed,
                Origin = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunOrigin.Event,
                FeatureId = "feature-ui-activity",
                Limit = 1
            },
            call);
        var projected = Assert.Single(list.Runs);
        var detail = await service.GetRun(
            new GetRunRequest { RunId = matching.RunId },
            call);

        Assert.Equal(projected, detail.Run);
        Assert.Equal(matching.RunId, projected.RunId);
        Assert.Equal("feature-ui-activity", projected.FeatureId);
        Assert.Equal("Summarize safe Activity", projected.FeatureName);
        Assert.Equal(installationId.Value, projected.InstallationId);
        Assert.Equal(release.Value, projected.ReleaseDigest);
        Assert.Equal("event.gmail", projected.InputKind);
        Assert.Equal(McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunOrigin.Event, projected.Origin);
        Assert.Equal("automation-ui-activity", projected.OriginReference.AutomationId);
        Assert.Equal(McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunStatus.Failed, projected.Status);
        Assert.Equal(McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunAuthorityState.Paused, projected.AuthorityState);
        Assert.Equal(occurredAt.ToUnixTimeMilliseconds(), projected.OccurredAtUnixMs);
        Assert.Equal(occurredAt.AddSeconds(1).ToUnixTimeMilliseconds(), projected.StartedAtUnixMs);
        Assert.False(projected.HasCompletedAtUnixMs);
        Assert.Equal(occurredAt.AddMinutes(1).ToUnixTimeMilliseconds(), projected.RetryAtUnixMs);
        Assert.Equal(2, projected.Attempts);
        Assert.Equal("surface-ui-activity", projected.ResultSurfaceReference);
        Assert.Equal("The Feature could not complete.", projected.SafeFailure);
        Assert.Equal("Review the Feature before retrying.", projected.FailureGuidance);
        Assert.Equal("trace-ui-activity", projected.TraceReference);
        Assert.DoesNotContain("provider-payload-secret", list.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("owner", list.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("principal", list.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_requests_map_invalid_missing_and_unsafe_projection_failures_to_safe_statuses()
    {
        var occurredAt = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var installationId = new FeatureInstallationId("installation-ui-activity-errors");
        var release = new ReleaseDigest(new string('b', 64));
        var invalidProjection = ActivityRun(
            "run-ui-activity-invalid-projection",
            installationId,
            release,
            FeatureRunOrigin.Direct,
            FeatureRunStatus.Running,
            FeatureRunAuthorityState.Authorized,
            occurredAt,
            6);
        var logger = new CapturingLogger<DigitalBrainUiEndpoints>();
        var (service, _) = CreateService(
            grants: new HashSet<string>(["brain.read"], StringComparer.Ordinal),
            endpointLogger: logger,
            queries: ActivityQueries(installationId, release, invalidProjection));
        var call = await AuthenticatedCallAsync(service);
        var invalidRequests = new[]
        {
            new ListActivityRequest
            {
                Status = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunStatus.Unspecified
            },
            new ListActivityRequest
            {
                Origin = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRunOrigin.Unspecified
            },
            new ListActivityRequest { FeatureId = " invalid-feature" },
            new ListActivityRequest { Limit = DigitalBrainQueryService.MaximumListLimit + 1 }
        };

        foreach (var request in invalidRequests)
        {
            var invalid = await Assert.ThrowsAsync<RpcException>(() => service.ListActivity(request, call));
            Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);
            Assert.Equal("The Activity request is invalid.", invalid.Status.Detail);
        }

        var missing = await Assert.ThrowsAsync<RpcException>(() => service.GetRun(
            new GetRunRequest { RunId = "run-ui-activity-missing" },
            call));
        var unsafeProjection = await Assert.ThrowsAsync<RpcException>(() => service.GetRun(
            new GetRunRequest { RunId = invalidProjection.RunId },
            call));

        Assert.Equal(StatusCode.NotFound, missing.StatusCode);
        Assert.Equal("The requested Run was not found.", missing.Status.Detail);
        Assert.Equal(StatusCode.Internal, unsafeProjection.StatusCode);
        Assert.Equal("Activity could not be loaded.", unsafeProjection.Status.Detail);
        Assert.DoesNotContain("provider-payload-secret", unsafeProjection.Status.Detail, StringComparison.Ordinal);
        Assert.Contains("An Activity response projection failed safely.", logger.Messages);
    }

    [Fact]
    public async Task GetFeatureDraft_returns_only_the_authenticated_Owner_scope()
    {
        var owner = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-get-draft",
            "Read the owner-local Feature Draft",
            DateTimeOffset.UtcNow,
            "conversation-ui-get-draft"));
        var otherHub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(new BrainOwnerId("other-owner")));
        var otherDraft = await otherHub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-get-other-draft",
            "Keep another owner's Draft private",
            DateTimeOffset.UtcNow,
            "conversation-ui-get-other-draft"));
        var (service, _) = CreateService(grants: new HashSet<string>(["brain.read", "ui.action", "feature.manage"], StringComparer.Ordinal));
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);

        var reply = await service.GetFeatureDraft(new GetFeatureDraftRequest { DraftId = draft.DraftId.Value }, call);
        var crossOwner = await Assert.ThrowsAsync<RpcException>(() => service.GetFeatureDraft(
            new GetFeatureDraftRequest { DraftId = otherDraft.DraftId.Value },
            call));
        var absent = await Assert.ThrowsAsync<RpcException>(() => service.GetFeatureDraft(
            new GetFeatureDraftRequest { DraftId = "proposal-absent" },
            call));

        Assert.Equal(draft.DraftId.Value, reply.Draft.DraftId);
        Assert.Equal(draft.Goal, reply.Draft.Goal);
        Assert.Equal(draft.Revision, reply.Draft.Revision);
        Assert.Null(reply.Recovery);
        Assert.Equal(StatusCode.NotFound, crossOwner.StatusCode);
        Assert.Equal(absent.Status, crossOwner.Status);
    }

    [Fact]
    public async Task ListFeatures_returns_the_authenticated_Owner_drafts()
    {
        var owner = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-list-features",
            "List an owner-local Feature Draft",
            DateTimeOffset.UtcNow,
            "conversation-ui-list-features"));
        var otherHub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(new BrainOwnerId("other-owner")));
        await otherHub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-list-other-features",
            "Do not leak another owner's Draft",
            DateTimeOffset.UtcNow,
            "conversation-ui-list-other-features"));
        var (service, _) = CreateService(grants: new HashSet<string>(["brain.read", "ui.action", "feature.manage"], StringComparer.Ordinal));
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);

        var reply = await service.ListFeatures(new ListFeaturesRequest(), call);

        var listed = Assert.Single(reply.Features);
        Assert.Equal(draft.DraftId.Value, listed.DraftId);
        Assert.Equal(draft.Goal, listed.Goal);
    }

    [Fact]
    public async Task GetFeatureDraft_recovers_a_durable_partial_installation_for_the_authenticated_owner()
    {
        var ownerId = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-recovery-partial",
            "Recover an interrupted Feature installation",
            DateTimeOffset.UtcNow,
            "conversation-ui-recovery-partial"));
        var release = new FeatureReleaseMetadata(
            new ReleaseDigest(new string('7', 64)),
            FeatureDraftAuthoringTransitions.SourceReference(draft.Source),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            [],
            draft.Source);
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(release.Digest, draft.Source, 1, DateTimeOffset.UtcNow),
            draft.Revision,
            "verify-ui-recovery-partial"));
        var catalog = new RecordingFeatureArtifactCatalog(release);
        var lifecycle = new LiveFeatureLifecycleRail(Cluster.Client, ownerId, _publicationVerifier)
        {
            FailAfter = "propose"
        };
        var authoring = new FeatureAuthoringService(
            Cluster.Client,
            new UnusedBuildEndpoint(),
            catalog,
            lifecycle,
            TimeProvider.System,
            new StaticFeatureCapabilityCatalog([
                new CapabilityDescriptor(
                    "capability.read",
                    1,
                    "Read",
                    "Read a value.",
                    ["Read a value."],
                    [],
                    ["google"],
                    CapabilityOrigin.Integration,
                    CapabilityOperationKind.Query,
                    true)
            ]));
        var (service, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: authoring);
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);
        var review = await service.ReviewFeatureAccess(
            new ReviewFeatureAccessRequest
            {
                DraftId = draft.DraftId.Value,
                ExpectedRevision = draft.Revision,
                InstallationId = "installation-ui-recovery-partial",
                ReleaseDigest = release.Digest.Value
            },
            call);
        var install = new InstallFeatureVersionRequest
        {
            DraftId = draft.DraftId.Value,
            ExpectedRevision = draft.Revision,
            InstallationId = "installation-ui-recovery-partial",
            ReleaseDigest = release.Digest.Value,
            DecisionId = "decision-ui-recovery-partial",
            IdempotencyId = "install-ui-recovery-partial"
        };
        install.Grants.Add(review.Grants);
        install.Subscriptions.Add(review.Subscriptions);

        var interrupted = await Assert.ThrowsAsync<RpcException>(() =>
            service.InstallFeatureVersion(install, call));
        var recovered = await service.GetFeatureDraft(
            new GetFeatureDraftRequest { DraftId = draft.DraftId.Value },
            call);

        Assert.Equal(StatusCode.Unavailable, interrupted.StatusCode);
        Assert.NotNull(recovered.Recovery);
        Assert.False(recovered.Recovery.Installed);
        Assert.Equal(release.Digest.Value, recovered.Recovery.Release.Digest);
        Assert.Null(recovered.Recovery.Release.Source);
        Assert.Equal(release.SourceReference, recovered.Recovery.Verification.SourceReference);
        Assert.Single(recovered.Recovery.Verification.Scenarios);
        Assert.Single(recovered.Recovery.Verification.Artifacts);
        Assert.Equal("capability.read", Assert.Single(recovered.Recovery.Grants).CapabilityId);
        Assert.Equal("manual", Assert.Single(recovered.Recovery.Subscriptions));
        Assert.Equal("decision-ui-recovery-partial", recovered.Recovery.DecisionId);
        Assert.Equal("install-ui-recovery-partial", recovered.Recovery.IdempotencyId);
        Assert.Null(recovered.Recovery.PreviousRelease);
        Assert.False(recovered.Recovery.RollbackAvailable);
        Assert.False(recovered.Recovery.Paused);
        Assert.False(recovered.Recovery.HasPauseReason);
        Assert.NotNull(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
        Assert.Equal(1, lifecycle.ProposeCount);
        Assert.Equal(0, lifecycle.DecideCount);
        Assert.Equal(0, lifecycle.GrantCount);
        Assert.Equal(0, lifecycle.InstallCount);

        var malformedReset = await Assert.ThrowsAsync<RpcException>(() =>
            service.ResetFeatureDraftInstallation(
                new ResetFeatureDraftInstallationRequest { DraftId = draft.DraftId.Value },
                call));
        Assert.Equal(StatusCode.InvalidArgument, malformedReset.StatusCode);
        Assert.Equal("The Feature request is invalid.", malformedReset.Status.Detail);
        Assert.NotNull(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));

        var resetRequest = new ResetFeatureDraftInstallationRequest
        {
            DraftId = draft.DraftId.Value,
            IdempotencyId = "reset-ui-recovery-partial"
        };
        var reset = await service.ResetFeatureDraftInstallation(resetRequest, call);
        var replay = await service.ResetFeatureDraftInstallation(resetRequest, call);

        Assert.Equal(draft.DraftId.Value, reset.Draft.DraftId);
        Assert.Equal(reset, replay);
        Assert.Null(reset.Recovery);
        Assert.Null(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
        Assert.Null(await hub.ReadDraftInstallationResetAsync(draft.DraftId));
    }

    [Fact]
    public void Legacy_Feature_Draft_projection_omits_the_missing_conversation_marker()
    {
        var now = DateTimeOffset.UtcNow;
        var draft = new FeatureDraft(
            new FeatureDraftId("proposal-ui-legacy"),
            new OriginatingRequest(
                "operation-ui-legacy",
                FeatureDraft.LegacyMissingConversationId,
                "Read a migrated Feature Draft"),
            "Read a migrated Feature Draft",
            "draft",
            new FeatureBehavior([
                new FeatureScenario("legacy-scenario", "Legacy", "a Draft was migrated", "it is read", "the sentinel stays private")
            ]),
            new FeatureSourceSnapshot(
                "src/Legacy/Legacy.csproj",
                "tests/Legacy.Scenarios/Legacy.Scenarios.csproj",
                [
                    new FeatureSourceFile("src/Legacy/Legacy.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                    new FeatureSourceFile("tests/Legacy.Scenarios/Legacy.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
                ]),
            null,
            null,
            0,
            now,
            now);
        var projection = typeof(DigitalBrainUiEndpoints)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "ToReply" && method.GetParameters() is [{ ParameterType: var type }] &&
                type == typeof(FeatureDraft));

        var reply = Assert.IsType<GrpcFeatureDraft>(projection.Invoke(null, [draft]));
        var presence = reply.OriginatingRequest.GetType().GetProperty("HasConversationId");

        Assert.NotNull(presence);
        Assert.False(Assert.IsType<bool>(presence.GetValue(reply.OriginatingRequest)));
        Assert.DoesNotContain(FeatureDraft.LegacyMissingConversationId, reply.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviseFeatureDraft_replays_and_maps_stale_or_malformed_commands_safely()
    {
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(new BrainOwnerId("owner")));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-revise-draft",
            "Revise a Feature Draft through the typed endpoint",
            DateTimeOffset.UtcNow,
            "conversation-ui-revise-draft"));
        var (service, _) = CreateService(grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal));
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);
        var request = new ReviseFeatureDraftRequest
        {
            DraftId = draft.DraftId.Value,
            ExpectedRevision = draft.Revision,
            IdempotencyId = "ui-revise-replay",
            ReviseBehavior = new ReviseFeatureBehaviorInput
            {
                Behavior = new GrpcFeatureBehavior
                {
                    Scenarios =
                    {
                        new GrpcFeatureScenario
                        {
                            ScenarioId = "ui-revise",
                            Name = "Typed revision",
                            Given = "an authenticated owner",
                            When = "Behavior is revised",
                            Then = "the exact command replays"
                        }
                    }
                }
            }
        };

        var first = await service.ReviseFeatureDraft(request, call);
        var replay = await service.ReviseFeatureDraft(request, call);
        var stale = request.Clone();
        stale.IdempotencyId = "ui-revise-stale";
        var staleException = await Assert.ThrowsAsync<RpcException>(() => service.ReviseFeatureDraft(stale, call));
        var maximumRevision = request.Clone();
        maximumRevision.ExpectedRevision = long.MaxValue;
        maximumRevision.IdempotencyId = "ui-revise-max";
        var maximumRevisionException = await Assert.ThrowsAsync<RpcException>(() =>
            service.ReviseFeatureDraft(maximumRevision, call));
        var missingRevision = request.Clone();
        missingRevision.ClearExpectedRevision();
        var missingRevisionException = await Assert.ThrowsAsync<RpcException>(() => service.ReviseFeatureDraft(missingRevision, call));
        var missingCommand = request.Clone();
        missingCommand.ClearCommand();
        var missingCommandException = await Assert.ThrowsAsync<RpcException>(() => service.ReviseFeatureDraft(missingCommand, call));

        Assert.Equal(1, first.Draft.Revision);
        Assert.Equal(request.ReviseBehavior.Behavior, first.Draft.Behavior);
        Assert.Equal(first.Draft.UpdatedAtUnixMs, replay.Draft.UpdatedAtUnixMs);
        Assert.Equal(StatusCode.Aborted, staleException.StatusCode);
        Assert.Equal("The Feature Draft changed. Reload it and retry.", staleException.Status.Detail);
        Assert.Equal(StatusCode.Aborted, maximumRevisionException.StatusCode);
        Assert.Equal("The Feature Draft changed. Reload it and retry.", maximumRevisionException.Status.Detail);
        Assert.Equal(StatusCode.InvalidArgument, missingRevisionException.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, missingCommandException.StatusCode);
    }

    [Fact]
    public async Task Suggest_verify_and_install_live_product_Rpcs_succeed_and_replay_through_the_full_service_boundary()
    {
        var ownerId = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-live-product",
            "Ship the live typed Feature product",
            DateTimeOffset.UtcNow,
            "conversation-ui-live-product"));
        var release = new FeatureReleaseMetadata(
            new ReleaseDigest(new string('a', 64)),
            FeatureDraftAuthoringTransitions.SourceReference(draft.Source),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            [],
            draft.Source);
        var builds = new RecordingFeatureBuildEndpoint(
            new FeatureBuildArtifact(release, new DigitalBrain.FeatureBuilder.FeatureScenarioResult(1, 1, 0, 0)));
        var catalog = new RecordingFeatureArtifactCatalog(release);
        var lifecycle = new LiveFeatureLifecycleRail(
            Cluster.Client,
            ownerId,
            _publicationVerifier);
        var authoring = new FeatureAuthoringService(
            Cluster.Client,
            builds,
            catalog,
            lifecycle,
            TimeProvider.System,
            new StaticFeatureCapabilityCatalog([
                new CapabilityDescriptor(
                    "capability.read",
                    1,
                    "Read",
                    "Read a value.",
                    ["Read a value."],
                    [],
                    ["google"],
                    CapabilityOrigin.Integration,
                    CapabilityOperationKind.Query,
                    true)
            ]));
        _chatClient!.Response = LiveSuggestionResponse();
        var (service, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: authoring);
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);

        var suggestion = await service.SuggestFeatureChange(
            new SuggestFeatureChangeRequest
            {
                DraftId = draft.DraftId.Value,
                ExpectedRevision = draft.Revision,
                Guidance = new string('g', 4096),
                SuggestionId = "suggestion-ui-live"
            },
            call);
        var afterSuggestion = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        var verifyRequest = new VerifyFeatureDraftRequest
        {
            DraftId = draft.DraftId.Value,
            ExpectedRevision = draft.Revision,
            IdempotencyId = "verify-ui-live"
        };
        var verified = await service.VerifyFeatureDraft(verifyRequest, call);
        var verifiedReplay = await service.VerifyFeatureDraft(verifyRequest, call);
        var reviewRequest = new ReviewFeatureAccessRequest
        {
            DraftId = verified.Draft.DraftId,
            ExpectedRevision = verified.Draft.Revision,
            InstallationId = "installation-ui-live",
            ReleaseDigest = release.Digest.Value
        };
        var accessReview = await service.ReviewFeatureAccess(reviewRequest, call);
        var installRequest = new InstallFeatureVersionRequest
        {
            DraftId = reviewRequest.DraftId,
            ExpectedRevision = reviewRequest.ExpectedRevision,
            InstallationId = reviewRequest.InstallationId,
            ReleaseDigest = reviewRequest.ReleaseDigest,
            DecisionId = "decision-ui-live",
            IdempotencyId = "install-ui-live"
        };
        installRequest.Grants.Add(accessReview.Grants);
        installRequest.Subscriptions.Add(accessReview.Subscriptions);
        var installed = await service.InstallFeatureVersion(installRequest, call);
        var installedReplay = await service.InstallFeatureVersion(installRequest, call);
        var recovered = await service.GetFeatureDraft(
            new GetFeatureDraftRequest { DraftId = draft.DraftId.Value },
            call);
        var detail = await service.GetFeature(
            new GetFeatureRequest { FeatureId = draft.DraftId.Value },
            call);
        var releaseSource = await service.GetFeatureReleaseSource(
            new GetFeatureReleaseSourceRequest
            {
                FeatureId = detail.FeatureId,
                InstallationId = detail.InstallationId,
                ReleaseDigest = detail.ActiveRelease.Digest,
                SourceReference = detail.ActiveRelease.SourceReference
            },
            call);

        Assert.Equal(draft.DraftId.Value, suggestion.Patch.DraftId);
        Assert.Equal(draft.Revision, suggestion.Patch.BaseRevision);
        Assert.Equal(draft.Revision, afterSuggestion.Revision);
        Assert.Null(afterSuggestion.Verification);
        Assert.Equal(verified, verifiedReplay);
        Assert.Equal(draft.Revision + 1, verified.Draft.Revision);
        Assert.Equal(release.Digest.Value, verified.Release.Digest);
        Assert.Equal(release.Digest.Value, accessReview.Release.Digest);
        Assert.Equal(release.SourceReference, accessReview.Release.SourceReference);
        Assert.Null(accessReview.Release.Source);
        Assert.Equal("capability.read", Assert.Single(accessReview.Grants).CapabilityId);
        Assert.Equal("manual", Assert.Single(accessReview.Subscriptions));
        Assert.Equal(installed, installedReplay);
        Assert.Equal("installation-ui-live", installed.InstallationId);
        Assert.Equal(McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftStatus.Installed, installed.Draft.Status);
        Assert.Equal(release.Digest.Value, installed.Release.Digest);
        Assert.Null(installed.Release.Source);
        Assert.Equal("capability.read", Assert.Single(installed.ActiveGrants).CapabilityId);
        Assert.Equal("manual", Assert.Single(installed.Subscriptions));
        Assert.NotNull(recovered.Recovery);
        Assert.True(recovered.Recovery.Installed);
        Assert.Equal(release.Digest.Value, recovered.Recovery.Release.Digest);
        Assert.Null(recovered.Recovery.Release.Source);
        Assert.Equal(release.SourceReference, recovered.Recovery.Verification.SourceReference);
        Assert.Single(recovered.Recovery.Verification.Scenarios);
        Assert.Empty(recovered.Recovery.Verification.Artifacts);
        Assert.False(recovered.Recovery.HasDecisionId);
        Assert.False(recovered.Recovery.HasIdempotencyId);
        Assert.False(recovered.Recovery.RollbackAvailable);
        Assert.Null(recovered.Recovery.PreviousRelease);
        Assert.Equal(draft.DraftId.Value, detail.FeatureId);
        Assert.Equal(release.Digest.Value, detail.ActiveRelease.Digest);
        Assert.Equal("installation-ui-live", detail.InstallationId);
        Assert.True(detail.Revision > 0);
        Assert.Null(detail.ActiveRelease.Source);
        Assert.Null(detail.PreviousRelease);
        Assert.False(detail.RollbackAvailable);
        Assert.Equal(detail.FeatureId, releaseSource.FeatureId);
        Assert.Equal(detail.InstallationId, releaseSource.InstallationId);
        Assert.Equal(detail.ActiveRelease.Digest, releaseSource.ReleaseDigest);
        Assert.Equal(detail.ActiveRelease.SourceReference, releaseSource.SourceReference);
        Assert.Equal(draft.Source.Files.Select(file => file.Content), releaseSource.Source.Files.Select(file => file.Content));
        Assert.Equal(1, _chatClient.CallCount);
        Assert.Equal(1, builds.CallCount);
        Assert.Equal(7, catalog.CallCount);
        Assert.Equal(7, catalog.SourceCallCount);
        Assert.Equal(1, lifecycle.InstallCount);
        Assert.Equal(1, lifecycle.RepublishCount);
    }

    [Fact]
    public async Task ResumeOriginatingRequest_restores_the_persisted_conversation_and_prompt_once_per_stable_intent()
    {
        var fixture = await CreateInstalledResumeFixtureAsync();
        var request = new ResumeOriginatingRequestRequest
        {
            DraftId = fixture.Draft.DraftId,
            ExpectedRevision = fixture.Draft.Revision,
            IdempotencyId = "run-originating-request-a"
        };

        var resumed = await fixture.Service.ResumeOriginatingRequest(request, fixture.Call);
        var conversation = Cluster.Client.GetGrain<IConversationNeuron>(RuntimeStateKeys.Conversation(
            fixture.OwnerId,
            fixture.ActorId,
            fixture.ConversationId));
        var state = await conversation.ReadAsync();
        var operation = Assert.Single(state.Operations, candidate =>
            string.Equals(candidate.CommandId, request.IdempotencyId, StringComparison.Ordinal));
        var turn = Assert.Single(state.Turns, candidate =>
            candidate.Kind == ConversationTurnKind.User &&
            string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal));
        var claim = await conversation.TryClaimOperationAsync(
            state.Revision,
            operation.OperationId,
            "resume-test-worker",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
        var replayed = await fixture.Service.ResumeOriginatingRequest(request, fixture.Call);
        var replayedState = await conversation.ReadAsync();

        Assert.True(claim.Acquired);
        Assert.Equal(ConversationOperationStatus.Running, claim.Operation!.Status);
        Assert.True(claim.Operation.Version > resumed.Version);
        Assert.Single(replayedState.Operations, candidate =>
            string.Equals(candidate.CommandId, request.IdempotencyId, StringComparison.Ordinal));
        Assert.Equal(request.IdempotencyId, resumed.CommandId);
        Assert.Equal(resumed, replayed);
        Assert.Equal(operation.OperationId, resumed.OperationId);
        Assert.Equal(fixture.Prompt, turn.Text);
        Assert.Equal(InoOperationPhase.Accepted.ToString(), resumed.Phase);
        Assert.True(resumed.Version >= 1);
    }

    [Fact]
    public async Task ResumeOriginatingRequest_rejects_revision_actor_and_persisted_identity_mismatches()
    {
        var fixture = await CreateInstalledResumeFixtureAsync();
        var stale = new ResumeOriginatingRequestRequest
        {
            DraftId = fixture.Draft.DraftId,
            ExpectedRevision = fixture.Draft.Revision - 1,
            IdempotencyId = "run-stale"
        };
        var staleFailure = await Assert.ThrowsAsync<RpcException>(() =>
            fixture.Service.ResumeOriginatingRequest(stale, fixture.Call));

        var (otherActorService, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: fixture.Authoring,
            actorId: new ActorId("other-principal"));
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var otherBootstrap = await otherActorService.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var otherActorCall = TestServerCallContext.WithHeaders(
            ("x-v2-session", otherBootstrap.AccessToken),
            audience);
        var actorFailure = await Assert.ThrowsAsync<RpcException>(() =>
            otherActorService.ResumeOriginatingRequest(
                new ResumeOriginatingRequestRequest
                {
                    DraftId = fixture.Draft.DraftId,
                    ExpectedRevision = fixture.Draft.Revision,
                    IdempotencyId = "run-other-actor"
                },
                otherActorCall));

        var identityFixture = await CreateInstalledResumeFixtureAsync(
            "ino-" + new string('f', 64),
            operationSuffix: "identity-mismatch");
        var identityFailure = await Assert.ThrowsAsync<RpcException>(() =>
            identityFixture.Service.ResumeOriginatingRequest(
                new ResumeOriginatingRequestRequest
                {
                    DraftId = identityFixture.Draft.DraftId,
                    ExpectedRevision = identityFixture.Draft.Revision,
                    IdempotencyId = "run-identity-mismatch"
                },
                identityFixture.Call));

        Assert.Equal(StatusCode.Aborted, staleFailure.StatusCode);
        Assert.Equal(StatusCode.FailedPrecondition, actorFailure.StatusCode);
        Assert.Equal(StatusCode.FailedPrecondition, identityFailure.StatusCode);
    }

    [Fact]
    public async Task GetConversationContext_returns_only_the_authenticated_owner_request_without_logging_it()
    {
        const string exactRequest = "Compare the retained request exactly, including punctuation: alpha/beta?";
        const string requestId = "request-chat-context-owned";
        var conversationId = "ino-" + new string('a', 64);
        var context = new RuntimeRequestContext(
            new BrainOwnerId("owner"),
            new ActorId("principal"),
            new SessionId("chat-context-seed-session"),
            AuthAssurance.Oidc,
            requestId,
            null,
            new HashSet<string>(StringComparer.Ordinal),
            conversationId);
        await new ConversationStateClient(Cluster.Client, TimeProvider.System)
            .BeginAsync(context, "command-chat-context-owned", exactRequest);
        var logger = new CapturingLogger<UiGrpcService>();
        var (service, _) = CreateService(serviceLogger: logger);
        var call = await AuthenticatedCallAsync(service);

        var reply = await service.GetConversationContext(
            new GetConversationContextRequest
            {
                ConversationId = conversationId,
                RequestId = requestId
            },
            call);

        Assert.Equal(conversationId, reply.ConversationId);
        Assert.Equal(requestId, reply.RequestId);
        Assert.Equal(exactRequest, reply.RequestText);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(exactRequest, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetConversationContext_maps_missing_and_wrong_owner_requests_to_the_same_safe_failure()
    {
        const string exactRequest = "This request belongs only to the original owner.";
        const string requestId = "request-chat-context-private";
        var conversationId = "ino-" + new string('b', 64);
        var context = new RuntimeRequestContext(
            new BrainOwnerId("owner"),
            new ActorId("principal"),
            new SessionId("chat-context-private-session"),
            AuthAssurance.Oidc,
            requestId,
            null,
            new HashSet<string>(StringComparer.Ordinal),
            conversationId);
        await new ConversationStateClient(Cluster.Client, TimeProvider.System)
            .BeginAsync(context, "command-chat-context-private", exactRequest);
        var logger = new CapturingLogger<UiGrpcService>();
        var (service, _) = CreateService(serviceLogger: logger);
        var call = await AuthenticatedCallAsync(service);
        var missing = await Assert.ThrowsAsync<RpcException>(() => service.GetConversationContext(
            new GetConversationContextRequest
            {
                ConversationId = "ino-" + new string('c', 64),
                RequestId = requestId
            },
            call));
        var (wrongOwnerService, _) = CreateService(
            ownerId: new BrainOwnerId("other-owner"),
            serviceLogger: logger);
        var wrongOwnerCall = await AuthenticatedCallAsync(wrongOwnerService);

        var wrongOwner = await Assert.ThrowsAsync<RpcException>(() => wrongOwnerService.GetConversationContext(
            new GetConversationContextRequest
            {
                ConversationId = conversationId,
                RequestId = requestId
            },
            wrongOwnerCall));

        Assert.Equal(StatusCode.NotFound, missing.StatusCode);
        Assert.Equal(missing.Status, wrongOwner.Status);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(exactRequest, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_verification_returns_safe_scenario_evidence_without_publishing_a_release()
    {
        var ownerId = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-failed-verification",
            "Explain a failed Feature scenario safely",
            DateTimeOffset.UtcNow,
            "conversation-ui-failed-verification"));
        var release = new FeatureReleaseMetadata(
            new ReleaseDigest(new string('f', 64)),
            $"sha256:{new string('f', 64)}",
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            []);
        var builds = new RecordingFeatureBuildEndpoint(
            new FeatureBuildArtifact(
                release,
                new DigitalBrain.FeatureBuilder.FeatureScenarioResult(1, 0, 1, 0)));
        var catalog = new RecordingFeatureArtifactCatalog(release);
        var lifecycle = new LiveFeatureLifecycleRail(Cluster.Client, ownerId, _publicationVerifier);
        var authoring = new FeatureAuthoringService(
            Cluster.Client,
            builds,
            catalog,
            lifecycle,
            TimeProvider.System);
        var (service, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: authoring);
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);

        var reply = await service.VerifyFeatureDraft(
            new VerifyFeatureDraftRequest
            {
                DraftId = draft.DraftId.Value,
                ExpectedRevision = draft.Revision,
                IdempotencyId = "verify-ui-failed"
            },
            call);

        Assert.Null(reply.Release);
        Assert.Equal(draft.Revision, reply.Draft.Revision);
        Assert.Equal(1, reply.Verification.Total);
        Assert.Equal(0, reply.Verification.Passed);
        Assert.Equal(1, reply.Verification.Failed);
        Assert.True(reply.Verification.VerifiedAtUnixMs > 0);
        var scenario = Assert.Single(reply.Verification.Scenarios);
        Assert.Equal("Scenario failed.", scenario.SafeFailure);
        Assert.DoesNotContain(" at ", scenario.SafeFailure, StringComparison.Ordinal);
        var persisted = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        Assert.Equal(draft.Revision, persisted.Revision);
        Assert.Null(persisted.Verification);
        Assert.Equal(1, builds.CallCount);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(0, lifecycle.MutationCount);
    }

    [Fact]
    public async Task Install_proto_validation_rejects_malformed_and_credential_bearing_requests_before_application_persistence()
    {
        var ownerId = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-invalid-install",
            "Reject malformed typed installation requests",
            DateTimeOffset.UtcNow,
            "conversation-ui-invalid-install"));
        var release = new FeatureReleaseMetadata(
            new ReleaseDigest(new string('b', 64)),
            $"sha256:{new string('b', 64)}",
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            []);
        var builds = new RecordingFeatureBuildEndpoint(
            new FeatureBuildArtifact(release, new DigitalBrain.FeatureBuilder.FeatureScenarioResult(1, 1, 0, 0)));
        var catalog = new RecordingFeatureArtifactCatalog(release);
        var lifecycle = new LiveFeatureLifecycleRail(Cluster.Client, ownerId, _publicationVerifier);
        var authoring = new FeatureAuthoringService(Cluster.Client, builds, catalog, lifecycle, TimeProvider.System);
        var logger = new CapturingLogger<DigitalBrainUiEndpoints>();
        var (service, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: authoring,
            endpointLogger: logger);
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);
        var valid = ValidInstallRequest(draft, release);
        var uppercaseDigest = valid.Clone();
        uppercaseDigest.ReleaseDigest = new string('A', 64);
        var excessiveGrants = valid.Clone();
        excessiveGrants.Grants.Clear();
        excessiveGrants.Grants.Add(Enumerable.Range(0, 33).Select(index => new GrpcFeatureGrant
        {
            CapabilityId = $"capability.{index}",
            CapabilityVersion = 1,
            ConstraintsJson = "{}"
        }));
        var excessiveSubscriptions = valid.Clone();
        excessiveSubscriptions.Subscriptions.Clear();
        excessiveSubscriptions.Subscriptions.Add(Enumerable.Range(0, 65).Select(index => $"subscription.{index}"));
        var credentialConstraint = valid.Clone();
        credentialConstraint.Grants[0].ConstraintsJson =
            "{\"allowedToolIds\":[\"capability.read\"],\"payload\":{\"secret_access_key\":\"credential-canary\"}}";

        foreach (var request in new[] { uppercaseDigest, excessiveGrants, excessiveSubscriptions, credentialConstraint })
        {
            var rejected = await Assert.ThrowsAsync<RpcException>(() => service.InstallFeatureVersion(request, call));
            Assert.Equal(StatusCode.InvalidArgument, rejected.StatusCode);
            Assert.Equal("The Feature request is invalid.", rejected.Status.Detail);
            Assert.DoesNotContain("canary", rejected.Status.Detail, StringComparison.Ordinal);
        }
        var notReady = await Assert.ThrowsAsync<RpcException>(() => service.InstallFeatureVersion(valid, call));

        Assert.Equal(StatusCode.FailedPrecondition, notReady.StatusCode);
        Assert.Equal("The Feature Draft is not ready for this operation.", notReady.Status.Detail);
        Assert.Equal(0, builds.CallCount);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(0, lifecycle.MutationCount);
        Assert.Null(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
        var unchanged = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        Assert.Equal(draft.Revision, unchanged.Revision);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("canary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_actor_mismatch_is_a_fixed_failed_precondition_without_reservation()
    {
        var ownerId = new BrainOwnerId("owner");
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-ui-actor-mismatch",
            "Reject a different installation actor",
            DateTimeOffset.UtcNow,
            "conversation-ui-actor-mismatch"));
        var release = new FeatureReleaseMetadata(
            new ReleaseDigest(new string('c', 64)),
            FeatureDraftAuthoringTransitions.SourceReference(draft.Source),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            []);
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            DigitalBrain.OrleansTests.Features.FeatureVerificationTestData.Passing(
                release.Digest,
                draft.Source,
                1,
                DateTimeOffset.UtcNow),
            draft.Revision,
            "verify-ui-actor-mismatch"));
        var request = ValidInstallRequest(draft, release);
        var grant = new FeatureGrantSpec(
            "capability.read",
            1,
            new ProviderConnectionId("connection-ui-live"),
            "{\"allowedToolIds\":[\"capability.read\"]}",
            "google");
        var registration = new FeatureInstallationRegistration(
            new FeatureInstallationId(request.InstallationId),
            release.Digest,
            request.Subscriptions.ToArray());
        var authority = new FeatureAuthoritySnapshot(
            registration.InstallationId,
            new ActorId("different-actor"),
            release.Digest,
            null,
            new GrantRevision(1),
            [grant],
            null,
            null,
            [],
            false,
            null);
        var lifecycle = new FixedInspectionLifecycleRail(new FeatureLifecycleInspection(
            1,
            [release],
            [],
            [new FeatureInstallationInspection(authority, registration, null)],
            [registration]));
        var authoring = new FeatureAuthoringService(
            Cluster.Client,
            new RecordingFeatureBuildEndpoint(new InvalidOperationException("must not build")),
            new RecordingFeatureArtifactCatalog(release),
            lifecycle,
            TimeProvider.System);
        var (service, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: authoring);
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);

        var rejected = await Assert.ThrowsAsync<RpcException>(() => service.InstallFeatureVersion(request, call));

        Assert.Equal(StatusCode.FailedPrecondition, rejected.StatusCode);
        Assert.Equal("The Feature Draft is not ready for this operation.", rejected.Status.Detail);
        Assert.Equal(0, lifecycle.MutationCount);
        Assert.Null(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
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
        Assert.Equal(9, composedCatalog.Count);
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
        var replayDraft = await hub.CreateDraftAsync(new CreateFeatureDraft(accepted.OperationId, Prompt, replayRequestedAt, state.Identity!.ConversationId));
        Assert.Equal(completed.Proposal.ProposalId, replayDraft.DraftId.Value);
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
        public string Response { get; set; } = "safe response";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Response))
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
        UiExternalIdentityOptions? externalOptions = null,
        IReadOnlySet<string>? grants = null,
        FeatureAuthoringService? authoring = null,
        FeatureSuggestionService? suggestion = null,
        ILogger<DigitalBrainUiEndpoints>? endpointLogger = null,
        ActorId? actorId = null,
        DigitalBrainQueryService? queries = null,
        BrainOwnerId? ownerId = null,
        ILogger<UiGrpcService>? serviceLogger = null)
    {
        var timeProvider = TimeProvider.System;
        var tokens = new SessionTokenService(Enumerable.Repeat((byte)13, 32).ToArray(), timeProvider);
        var sessions = new RuntimeSessionAuthority(Cluster.Client, tokens, timeProvider);
        var conversations = new ConversationStateClient(Cluster.Client, timeProvider);
        authoring ??= new FeatureAuthoringService(
            Cluster.Client,
            new UnusedBuildEndpoint(),
            new UnusedArtifactCatalog(),
            new UnusedFeatureLifecycleRail(),
            timeProvider);
        var endpoints = new DigitalBrainUiEndpoints(
            authoring,
            suggestion ?? new FeatureSuggestionService(Cluster.Client),
            endpointLogger ?? NullLogger<DigitalBrainUiEndpoints>.Instance,
            queries);
        var service = new UiGrpcService(
            new UiDevelopmentLoginAuthenticator(new UiDevelopmentLoginOptions(
                LoginUsername,
                 LoginPassword,
                 ownerId ?? new BrainOwnerId("owner"),
                 actorId ?? new ActorId("principal"),
                TimeSpan.FromMinutes(15),
                grants ?? new HashSet<string>(["brain.read", "ui.action"], StringComparer.Ordinal))),
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
            serviceLogger ?? NullLogger<UiGrpcService>.Instance,
            endpoints);
        return (service, sessions);
    }

    private async Task<ServerCallContext> AuthenticatedCallAsync(UiGrpcService service)
    {
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        return TestServerCallContext.WithHeaders(
            ("x-v2-session", bootstrap.AccessToken),
            audience);
    }

    private static Func<Task>[] ProductCalls(UiGrpcService service, ServerCallContext context) =>
    [
        () => service.GetFeatureDraft(new GetFeatureDraftRequest(), context),
        () => service.ResetFeatureDraftInstallation(new ResetFeatureDraftInstallationRequest(), context),
        () => service.ReviseFeatureDraft(new ReviseFeatureDraftRequest(), context),
        () => service.SuggestFeatureChange(new SuggestFeatureChangeRequest(), context),
        () => service.VerifyFeatureDraft(new VerifyFeatureDraftRequest(), context),
        () => service.ReviewFeatureAccess(new ReviewFeatureAccessRequest(), context),
        () => service.InstallFeatureVersion(new InstallFeatureVersionRequest(), context),
        () => service.ResumeOriginatingRequest(new ResumeOriginatingRequestRequest(), context),
        () => service.GetFeature(new GetFeatureRequest(), context),
        () => service.GetFeatureReleaseSource(new GetFeatureReleaseSourceRequest(), context),
        () => service.RollbackFeatureVersion(new RollbackFeatureVersionRequest(), context)
    ];

    private static Func<Task>[] ActivityCalls(UiGrpcService service, ServerCallContext context) =>
    [
        () => service.ListActivity(new ListActivityRequest(), context),
        () => service.GetRun(new GetRunRequest(), context)
    ];

    private static DigitalBrainQueryService ActivityQueries(
        FeatureInstallationId installationId,
        ReleaseDigest release,
        params FeatureRunSnapshot[] runs)
    {
        var authority = new FeatureAuthoritySnapshot(
            installationId,
            new ActorId("principal"),
            release,
            null,
            new GrantRevision(1),
            [],
            null,
            null,
            [],
            false,
            null,
            null,
            false,
            true);
        var registration = new FeatureInstallationRegistration(installationId, release, ["manual"]);
        var runtime = new FeatureInstallationSnapshot(
            installationId,
            release,
            null,
            "provider-payload-secret",
            false,
            null,
            [],
            null,
            [],
            [],
            [],
            1,
            [],
            null,
            runs);
        var inspection = new FeatureInstallationInspection(
            authority,
            registration,
            runtime,
            ActivityDraft(installationId, release));
        return new DigitalBrainQueryService(new FixedInspectionLifecycleRail(new FeatureLifecycleInspection(
            1,
            [],
            [],
            [inspection],
            [registration])));
    }

    private static FeatureDraft ActivityDraft(
        FeatureInstallationId installationId,
        ReleaseDigest release)
    {
        const string implementationProject = "src/UiActivityFeature/UiActivityFeature.csproj";
        const string scenarioProject = "tests/UiActivityFeature.Scenarios/UiActivityFeature.Scenarios.csproj";
        var now = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
        return new FeatureDraft(
            new FeatureDraftId("feature-ui-activity"),
            new OriginatingRequest(
                "operation-ui-activity",
                "conversation-ui-activity",
                "Summarize safe Activity"),
            "Summarize safe Activity",
            "Installed",
            new FeatureBehavior([
                new FeatureScenario(
                    "scenario-ui-activity",
                    "Project Activity",
                    "an installed Feature has Runs",
                    "Activity is requested",
                    "safe Run metadata is returned")
            ]),
            new FeatureSourceSnapshot(
                implementationProject,
                scenarioProject,
                [
                    new FeatureSourceFile(implementationProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                    new FeatureSourceFile(scenarioProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
                ]),
            new FeatureVerification(release, 1, 1, 0, 0, now),
            installationId,
            3,
            now,
            now.AddMinutes(1));
    }

    private static FeatureRunSnapshot ActivityRun(
        string runId,
        FeatureInstallationId installationId,
        ReleaseDigest release,
        FeatureRunOrigin origin,
        FeatureRunStatus status,
        FeatureRunAuthorityState authorityState,
        DateTimeOffset occurredAt,
        int attempts) => new(
        runId,
        installationId,
        release,
        origin switch
        {
            FeatureRunOrigin.Chat => "chat",
            FeatureRunOrigin.Schedule => "schedule.daily",
            FeatureRunOrigin.Event => "event.gmail",
            _ => "manual"
        },
        origin,
        origin switch
        {
            FeatureRunOrigin.Chat => new FeatureRunOriginReference(
                "conversation-ui-activity",
                "request-ui-activity",
                null),
            FeatureRunOrigin.Schedule or FeatureRunOrigin.Event => new FeatureRunOriginReference(
                null,
                null,
                "automation-ui-activity"),
            _ => null
        },
        status,
        authorityState,
        occurredAt,
        occurredAt.AddSeconds(1),
        null,
        status == FeatureRunStatus.Failed ? occurredAt.AddMinutes(1) : null,
        attempts,
        "surface-ui-activity",
        status == FeatureRunStatus.Failed ? "The Feature could not complete." : null,
        status == FeatureRunStatus.Failed ? "Review the Feature before retrying." : null,
        "trace-ui-activity");

    private async Task<InstalledResumeFixture> CreateInstalledResumeFixtureAsync(
        string? conversationId = null,
        string operationSuffix = "exact")
    {
        var ownerId = new BrainOwnerId("owner");
        var actorId = new ActorId("principal");
        var requestContext = new RuntimeRequestContext(
            ownerId,
            actorId,
            new SessionId("resume-fixture-session"),
            AuthAssurance.Oidc,
            "resume-fixture-correlation",
            null,
            new HashSet<string>(StringComparer.Ordinal));
        conversationId ??= InoConversationIdentity.From(requestContext);
        var prompt = $"Run the persisted request {operationSuffix}";
        var hub = Cluster.Client.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            $"operation-resume-{operationSuffix}",
            prompt,
            DateTimeOffset.UtcNow,
            conversationId));
        var release = new FeatureReleaseMetadata(
            new ReleaseDigest(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operationSuffix)))),
            FeatureDraftAuthoringTransitions.SourceReference(draft.Source),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            [],
            draft.Source);
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(release.Digest, draft.Source, 1, DateTimeOffset.UtcNow),
            draft.Revision,
            $"verify-resume-{operationSuffix}"));
        var catalog = new RecordingFeatureArtifactCatalog(release);
        var lifecycle = new LiveFeatureLifecycleRail(Cluster.Client, ownerId, _publicationVerifier);
        var authoring = new FeatureAuthoringService(
            Cluster.Client,
            new UnusedBuildEndpoint(),
            catalog,
            lifecycle,
            TimeProvider.System,
            new StaticFeatureCapabilityCatalog([
                new CapabilityDescriptor(
                    "capability.read",
                    1,
                    "Read",
                    "Read a value.",
                    ["Read a value."],
                    [],
                    ["google"],
                    CapabilityOrigin.Integration,
                    CapabilityOperationKind.Query,
                    true)
            ]));
        var (service, _) = CreateService(
            grants: new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
            authoring: authoring);
        var audience = ("x-v2-audience", SessionAudiences.Ui);
        var bootstrap = await service.BootstrapSession(
            new BootstrapSessionRequest { Username = LoginUsername, Password = LoginPassword },
            TestServerCallContext.WithHeaders(audience));
        var call = TestServerCallContext.WithHeaders(("x-v2-session", bootstrap.AccessToken), audience);
        var review = await service.ReviewFeatureAccess(
            new ReviewFeatureAccessRequest
            {
                DraftId = draft.DraftId.Value,
                ExpectedRevision = draft.Revision,
                InstallationId = $"installation-resume-{operationSuffix}",
                ReleaseDigest = release.Digest.Value
            },
            call);
        var install = new InstallFeatureVersionRequest
        {
            DraftId = draft.DraftId.Value,
            ExpectedRevision = draft.Revision,
            InstallationId = review.InstallationId,
            ReleaseDigest = release.Digest.Value,
            DecisionId = $"decision-resume-{operationSuffix}",
            IdempotencyId = $"install-resume-{operationSuffix}"
        };
        install.Grants.Add(review.Grants);
        install.Subscriptions.Add(review.Subscriptions);
        var installed = await service.InstallFeatureVersion(install, call);
        return new InstalledResumeFixture(
            service,
            call,
            authoring,
            installed.Draft,
            ownerId,
            actorId,
            conversationId,
            prompt);
    }

    private sealed record InstalledResumeFixture(
        UiGrpcService Service,
        ServerCallContext Call,
        FeatureAuthoringService Authoring,
        GrpcFeatureDraft Draft,
        BrainOwnerId OwnerId,
        ActorId ActorId,
        string ConversationId,
        string Prompt);

    private static InstallFeatureVersionRequest ValidInstallRequest(
        GrpcFeatureDraft draft,
        FeatureReleaseMetadata release) => ValidInstallRequest(draft.DraftId, draft.Revision, release);

    private static InstallFeatureVersionRequest ValidInstallRequest(
        FeatureDraft draft,
        FeatureReleaseMetadata release) => ValidInstallRequest(draft.DraftId.Value, draft.Revision, release);

    private static InstallFeatureVersionRequest ValidInstallRequest(
        string draftId,
        long revision,
        FeatureReleaseMetadata release)
    {
        var request = new InstallFeatureVersionRequest
        {
            DraftId = draftId,
            ExpectedRevision = revision,
            InstallationId = "installation-ui-live",
            ReleaseDigest = release.Digest.Value,
            DecisionId = "decision-ui-live",
            IdempotencyId = "install-ui-live"
        };
        request.Grants.Add(new GrpcFeatureGrant
        {
            CapabilityId = "capability.read",
            CapabilityVersion = 1,
            ConnectionId = "connection-ui-live",
            ConstraintsJson = "{\"allowedToolIds\":[\"capability.read\"]}",
            Provider = "google"
        });
        request.Subscriptions.Add("conversation.completed");
        return request;
    }

    private sealed class StaticFeatureCapabilityCatalog(IEnumerable<CapabilityDescriptor> descriptors)
        : FeatureCapabilityCatalog
    {
        private readonly CapabilityDescriptor[] _descriptors = descriptors.ToArray();

        public Task<IReadOnlyList<CapabilityDescriptor>> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CapabilityDescriptor>>(_descriptors);
        }
    }

    private static string LiveSuggestionResponse() => """
        {
          "summary": "Review the live typed change",
          "replacementBehavior": {
            "scenarios": [
              {
                "scenarioId": "scenario-ui-live",
                "name": "Ship the typed Feature",
                "given": "an owner has a Draft",
                "when": "the change is reviewed",
                "then": "the typed patch remains explicit"
              }
            ]
          },
          "replacementSource": {
            "implementationProjectPath": "src/UiLive/UiLive.csproj",
            "scenarioProjectPath": "tests/UiLive.Scenarios/UiLive.Scenarios.csproj",
            "files": [
              { "path": "src/UiLive/UiLive.csproj", "content": "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>" },
              { "path": "src/UiLive/Feature.cs", "content": "namespace RuntimeAuthored; public sealed class Feature;" },
              { "path": "tests/UiLive.Scenarios/UiLive.Scenarios.csproj", "content": "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>" }
            ]
          }
        }
        """;

    private sealed class RecordingFeatureBuildEndpoint : FeatureBuildEndpoint
    {
        private readonly FeatureBuildArtifact? _artifact;
        private readonly Exception? _failure;

        public RecordingFeatureBuildEndpoint(FeatureBuildArtifact artifact) => _artifact = artifact;
        public RecordingFeatureBuildEndpoint(Exception failure) => _failure = failure;
        public int CallCount { get; private set; }

        public Task<FeatureBuildArtifact> BuildAsync(
            FeatureBuildSubmission submission,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _failure is null
                ? Task.FromResult(_artifact!)
                : Task.FromException<FeatureBuildArtifact>(_failure);
        }
    }

    private sealed class RecordingFeatureArtifactCatalog(FeatureReleaseMetadata release) : FeatureArtifactCatalog
    {
        public int CallCount { get; private set; }
        public int SourceCallCount { get; private set; }

        public Task<FeatureReleaseMetadata> DemandReleaseAsync(
            ReleaseDigest digest,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(release);
        }

        public Task<FeatureSourceSnapshot> DemandSourceAsync(
            string sourceReference,
            CancellationToken cancellationToken = default)
        {
            SourceCallCount++;
            return Task.FromResult(release.Source ?? new FeatureSourceSnapshot(
                "src/UiLive/UiLive.csproj",
                "tests/UiLive.Scenarios/UiLive.Scenarios.csproj",
                [
                    new FeatureSourceFile("src/UiLive/UiLive.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                    new FeatureSourceFile("src/UiLive/Feature.cs", "namespace RuntimeAuthored; public sealed class Feature;"),
                    new FeatureSourceFile("tests/UiLive.Scenarios/UiLive.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
                ]));
        }
    }

    private sealed class LiveFeatureLifecycleRail(
        IClusterClient cluster,
        BrainOwnerId ownerId,
        TestFeaturePublicationVerifier verifier) : FeatureLifecycleRail
    {
        public int ProposeCount { get; private set; }
        public int DecideCount { get; private set; }
        public int GrantCount { get; private set; }
        public int InstallCount { get; private set; }
        public int RepublishCount { get; private set; }
        public string? FailAfter { get; set; }
        public int MutationCount => ProposeCount + DecideCount + GrantCount + InstallCount;

        public async Task<FeatureLifecycleInspection> InspectAsync(
            RuntimeRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var snapshot = await Hub.ReadAsync().WaitAsync(cancellationToken);
            var installations = new List<FeatureInstallationInspection>();
            foreach (var authority in snapshot.Authorities)
            {
                var registration = snapshot.Installations.SingleOrDefault(candidate =>
                    candidate.InstallationId == authority.InstallationId);
                var runtime = registration is null
                    ? null
                    : await cluster.GetGrain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(ownerId, authority.InstallationId))
                        .ReadAsync()
                        .WaitAsync(cancellationToken);
                installations.Add(new FeatureInstallationInspection(authority, registration, runtime));
            }
            return new FeatureLifecycleInspection(
                snapshot.Revision,
                snapshot.Releases,
                snapshot.Approvals,
                installations,
                snapshot.Installations);
        }

        public async Task<FeatureApprovalSnapshot> ProposeAsync(
            RuntimeRequestContext context,
            FeatureReleaseProposal proposal,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            ProposeCount++;
            var approval = await Hub.ProposeAsync(proposal, expectedRevision).WaitAsync(cancellationToken);
            Fail("propose");
            return approval;
        }

        public async Task<FeatureApprovalSnapshot> DecideAsync(
            RuntimeRequestContext context,
            FeatureApprovalDecision decision,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            DecideCount++;
            return await Hub.DecideAsync(decision, expectedRevision).WaitAsync(cancellationToken);
        }

        public async Task<FeatureAuthoritySnapshot> GrantAsync(
            RuntimeRequestContext context,
            FeatureInstallationId installationId,
            ReleaseDigest release,
            FeatureGrantSpec[] grants,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            GrantCount++;
            return await Hub.GrantAsync(
                new FeatureGrantRequest(installationId, release, context.ActorId, grants),
                expectedRevision).WaitAsync(cancellationToken);
        }

        public async Task<FeatureAuthoritySnapshot> InstallAsync(
            RuntimeRequestContext context,
            FeatureInstallationRegistration registration,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            InstallCount++;
            var authority = await Hub.InstallAsync(registration, expectedRevision).WaitAsync(cancellationToken);
            await PublishAsync(registration.InstallationId, cancellationToken);
            return authority;
        }

        public async Task<FeatureAuthoritySnapshot> RepublishAsync(
            RuntimeRequestContext context,
            FeatureInstallationRegistration registration,
            CancellationToken cancellationToken = default)
        {
            RepublishCount++;
            var snapshot = await Hub.ReadAsync().WaitAsync(cancellationToken);
            var authority = snapshot.Authorities.Single(candidate =>
                candidate.InstallationId == registration.InstallationId &&
                candidate.ActiveRelease == registration.Release);
            var durable = snapshot.Installations.Single(candidate =>
                candidate.InstallationId == registration.InstallationId);
            Assert.Equal(registration.InstallationId, durable.InstallationId);
            Assert.Equal(registration.Release, durable.Release);
            Assert.Equal(registration.Subscriptions, durable.Subscriptions);
            await PublishAsync(registration.InstallationId, cancellationToken);
            return authority;
        }

        private async Task PublishAsync(
            FeatureInstallationId installationId,
            CancellationToken cancellationToken)
        {
            var ticket = await Hub.PrepareActivePublicationAsync(installationId).WaitAsync(cancellationToken);
            var receipt = new FeaturePublicationReceipt(
                installationId,
                ticket.PublicationFence,
                ticket.AuthorityDigest,
                ticket.AccessDigest,
                Convert.ToHexStringLower(SHA256.HashData(
                    FeaturePublicationManifestCodec.Serialize(ownerId, ticket))));
            verifier.Allow(ownerId, ticket, receipt);
            await Hub.ConfirmActivePublicationAsync(receipt).WaitAsync(cancellationToken);
        }

        private IFeatureHubGrain Hub => cluster.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));

        private void Fail(string boundary)
        {
            if (!string.Equals(FailAfter, boundary, StringComparison.Ordinal)) return;
            FailAfter = null;
            throw new IOException($"Injected failure after {boundary}.");
        }
    }

    private sealed class FixedInspectionLifecycleRail(FeatureLifecycleInspection inspection) : FeatureLifecycleRail
    {
        public int MutationCount { get; private set; }

        public Task<FeatureLifecycleInspection> InspectAsync(
            RuntimeRequestContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(inspection);

        public Task<FeatureRunLifecycleInspection> InspectRunsAsync(
            RuntimeRequestContext context,
            FeatureRunReadRequest request,
            CancellationToken cancellationToken = default)
        {
            var projected = inspection.Installations.Select(candidate => new FeatureRunInstallationInspection(
                candidate.Authority,
                candidate.Registration,
                RunProjection(candidate.Runtime, request),
                candidate.Draft)).ToArray();
            return Task.FromResult(new FeatureRunLifecycleInspection(projected));
        }

        public Task<FeatureApprovalSnapshot> ProposeAsync(
            RuntimeRequestContext context,
            FeatureReleaseProposal proposal,
            long expectedRevision,
            CancellationToken cancellationToken = default) => Unexpected<FeatureApprovalSnapshot>();

        public Task<FeatureApprovalSnapshot> DecideAsync(
            RuntimeRequestContext context,
            FeatureApprovalDecision decision,
            long expectedRevision,
            CancellationToken cancellationToken = default) => Unexpected<FeatureApprovalSnapshot>();

        public Task<FeatureAuthoritySnapshot> GrantAsync(
            RuntimeRequestContext context,
            FeatureInstallationId installationId,
            ReleaseDigest release,
            FeatureGrantSpec[] grants,
            long expectedRevision,
            CancellationToken cancellationToken = default) => Unexpected<FeatureAuthoritySnapshot>();

        public Task<FeatureAuthoritySnapshot> InstallAsync(
            RuntimeRequestContext context,
            FeatureInstallationRegistration registration,
            long expectedRevision,
            CancellationToken cancellationToken = default) => Unexpected<FeatureAuthoritySnapshot>();

        public Task<FeatureAuthoritySnapshot> RepublishAsync(
            RuntimeRequestContext context,
            FeatureInstallationRegistration registration,
            CancellationToken cancellationToken = default) => Unexpected<FeatureAuthoritySnapshot>();

        private Task<T> Unexpected<T>()
        {
            MutationCount++;
            return Task.FromException<T>(new InvalidOperationException("Unexpected lifecycle mutation."));
        }

        private static FeatureRunCollectionSnapshot? RunProjection(
            FeatureInstallationSnapshot? runtime,
            FeatureRunReadRequest request)
        {
            if (runtime?.Runs is not { } runs)
                return null;
            return new FeatureRunCollectionSnapshot(
                runtime.InstallationId,
                runtime.ActiveRelease,
                runtime.Revision,
                runs
                    .Where(candidate => request.Status is null || candidate.Status == request.Status)
                    .Where(candidate => request.Origin is null || candidate.Origin == request.Origin)
                    .Where(candidate => request.RunId is null || string.Equals(candidate.RunId, request.RunId, StringComparison.Ordinal))
                    .OrderByDescending(candidate => candidate.CompletedAt ?? candidate.OccurredAt)
                    .ThenByDescending(candidate => candidate.OccurredAt)
                    .ThenBy(candidate => candidate.RunId, StringComparer.Ordinal)
                    .Take(request.Limit)
                    .ToArray());
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class UnusedBuildEndpoint : FeatureBuildEndpoint
    {
        public Task<FeatureBuildArtifact> BuildAsync(
            FeatureBuildSubmission submission,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedArtifactCatalog : FeatureArtifactCatalog
    {
        public Task<FeatureReleaseMetadata> DemandReleaseAsync(
            ReleaseDigest digest,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedFeatureLifecycleRail : FeatureLifecycleRail
    {
        public Task<FeatureApprovalSnapshot> ProposeAsync(
            RuntimeRequestContext context,
            FeatureReleaseProposal proposal,
            long expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureApprovalSnapshot> DecideAsync(
            RuntimeRequestContext context,
            FeatureApprovalDecision decision,
            long expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> GrantAsync(
            RuntimeRequestContext context,
            FeatureInstallationId installationId,
            ReleaseDigest release,
            FeatureGrantSpec[] grants,
            long expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> InstallAsync(
            RuntimeRequestContext context,
            FeatureInstallationRegistration registration,
            long expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> RepublishAsync(
            RuntimeRequestContext context,
            FeatureInstallationRegistration registration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureLifecycleInspection> InspectAsync(
            RuntimeRequestContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(new FeatureLifecycleInspection(
                0,
                [],
                [],
                Array.Empty<FeatureInstallationInspection>(),
                Array.Empty<FeatureInstallationRegistration>()));
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
