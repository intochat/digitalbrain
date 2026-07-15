extern alias McpProject;

using System.Reflection;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RuntimeProfile = DigitalBrain.Kernel.Contracts.Runtime.RuntimeProfile;
using SessionTokenService = DigitalBrain.Kernel.Contracts.Runtime.SessionTokenService;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using DigitalBrainUiEndpoints = McpProject::DigitalBrain.Mcp.DigitalBrainUiEndpoints;
using FeatureAuthoringService = McpProject::DigitalBrain.Mcp.FeatureAuthoringService;
using FeatureDraftRecoverySnapshot = McpProject::DigitalBrain.Mcp.FeatureDraftRecoverySnapshot;
using FeatureInstallationRecoverySnapshot = McpProject::DigitalBrain.Mcp.FeatureInstallationRecoverySnapshot;
using FeatureSuggestionService = McpProject::DigitalBrain.Mcp.FeatureSuggestionService;
using FeatureVerificationReview = McpProject::DigitalBrain.Mcp.FeatureVerificationReview;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using RuntimeTransportBoundary = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundary;
using RuntimeTransportBoundaryOptions = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundaryOptions;
using UiHostingExtensions = McpProject::DigitalBrain.Mcp.UiHostingExtensions;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;
using FeatureDraftReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftReply;
using FeatureAccessReviewReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureAccessReviewReply;
using FeatureInstallReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureInstallReply;
using FeatureReleaseSourceReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureReleaseSourceReply;
using FeatureReleaseReviewReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureReleaseReviewReply;
using FeatureReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureReply;
using GrpcFeatureBehavior = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureBehavior;
using GrpcFeatureDraft = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraft;
using GrpcFeatureDraftStatus = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftStatus;
using GrpcFeatureGrant = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureGrant;
using GrpcFeatureRelease = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureRelease;
using GrpcFeatureScenario = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureScenario;
using GrpcFeatureSourceFile = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceFile;
using GrpcFeatureSourceKind = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceKind;
using GrpcFeatureSourceSnapshot = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceSnapshot;
using GrpcFeatureVerification = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureVerification;
using GrpcOriginatingRequest = McpProject::DigitalBrain.V2.Ui.Grpc.OriginatingRequest;
using GetFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureDraftRequest;
using GetFeatureReleaseSourceRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureReleaseSourceRequest;
using GetFeatureRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetFeatureRequest;
using InstallFeatureVersionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.InstallFeatureVersionRequest;
using ReviewFeatureAccessRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviewFeatureAccessRequest;
using VerifyFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.VerifyFeatureDraftRequest;
using ReviseFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureDraftRequest;
using ReviseFeatureSourceInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureSourceInput;
using UiService = McpProject::DigitalBrain.V2.Ui.Grpc.DigitalBrainV2Ui;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiGrpcTransportLimitTests
{
    private const int EightMiB = 8 * 1024 * 1024;
    private const int SixteenMiB = 16 * 1024 * 1024;

    [Fact]
    public async Task Actual_gRPC_pipeline_transports_production_projected_valid_maxima_and_enforces_directional_limits()
    {
        var validSource = MaximumSourceSnapshot();
        var domainSource = ProductionRoundTrip(validSource);
        var fixture = MaximumValidFixture(domainSource);
        var recoveryReply = MaximumValidRecoveryReply(fixture);
        var service = new TransportUiService(
            recoveryReply,
            OversizedRecoveryReply(recoveryReply),
            MaximumValidVerificationReply(fixture),
            MaximumValidInstallReply(fixture),
            MaximumValidAccessReviewReply(fixture),
            MaximumValidFeatureReply(fixture),
            MaximumValidSourceReply(fixture));
        var bodyFeature = new RecordingMaxRequestBodySizeFeature();
        using var host = await CreateHostAsync(service, bodyFeature);
        var server = host.GetTestServer();
        using var channel = GrpcChannel.ForAddress(server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = server.CreateHandler(),
            MaxReceiveMessageSize = SixteenMiB,
            MaxSendMessageSize = SixteenMiB
        });
        var client = new UiService.DigitalBrainV2UiClient(channel);
        var valid = new ReviseFeatureDraftRequest
        {
            DraftId = "draft-valid-four-mib",
            ExpectedRevision = 0,
            IdempotencyId = "source-valid-four-mib",
            ReviseSource = new ReviseFeatureSourceInput { Source = validSource.Clone() }
        };
        var maximumReplySizes = new[]
        {
            service.VerificationReply.CalculateSize(),
            service.InstallReply.CalculateSize(),
            service.AccessReviewReply.CalculateSize(),
            service.FeatureReply.CalculateSize(),
            service.SourceReply.CalculateSize()
        };

        var revised = await client.ReviseFeatureDraftAsync(valid);
        var recovered = await client.GetFeatureDraftAsync(new GetFeatureDraftRequest
        {
            DraftId = fixture.Installed.Draft.DraftId.Value
        });
        var verified = await client.VerifyFeatureDraftAsync(new VerifyFeatureDraftRequest());
        var installed = await client.InstallFeatureVersionAsync(new InstallFeatureVersionRequest());
        var access = await client.ReviewFeatureAccessAsync(new ReviewFeatureAccessRequest());
        var detail = await client.GetFeatureAsync(new GetFeatureRequest());
        var source = await client.GetFeatureReleaseSourceAsync(new GetFeatureReleaseSourceRequest());

        Assert.NotNull(revised);
        Assert.Equal(service.RecoveryReply.CalculateSize(), recovered.CalculateSize());
        Assert.Equal(maximumReplySizes[0], verified.CalculateSize());
        Assert.Equal(maximumReplySizes[1], installed.CalculateSize());
        Assert.Equal(maximumReplySizes[2], access.CalculateSize());
        Assert.Equal(maximumReplySizes[3], detail.CalculateSize());
        Assert.Equal(maximumReplySizes[4], source.CalculateSize());
        Assert.True(valid.CalculateSize() > 4 * 1024 * 1024);
        Assert.True(valid.CalculateSize() < EightMiB);
        Assert.All(maximumReplySizes, size => Assert.InRange(size, 1, EightMiB - 1));
        Assert.True(maximumReplySizes[0] > 1024 * 1024);
        Assert.True(maximumReplySizes[1] > 4 * 1024 * 1024);
        Assert.True(maximumReplySizes[2] > 4 * 1024 * 1024);
        Assert.True(maximumReplySizes[4] > 4 * 1024 * 1024);
        Assert.InRange(recovered.CalculateSize(), EightMiB + 1, SixteenMiB);
        Assert.Equal(64, recovered.Draft.Source.Files.Count);
        Assert.Equal(32, recovered.Recovery.Grants.Count);
        Assert.Equal(64, recovered.Recovery.Subscriptions.Count);
        Assert.Equal(1024, recovered.Recovery.Verification.Scenarios.Count);
        Assert.Equal(32, recovered.Recovery.Verification.Artifacts.Count);
        Assert.Null(verified.Draft.Source);
        Assert.Empty(verified.Draft.Verification.Scenarios);
        Assert.Empty(verified.Draft.Verification.Artifacts);
        Assert.Equal(1024, verified.Verification.Scenarios.Count);
        Assert.NotNull(installed.Draft.Source);
        Assert.Empty(installed.Draft.Verification.Scenarios);
        Assert.Empty(installed.Draft.Verification.Artifacts);
        Assert.Null(installed.Release.Source);
        Assert.NotNull(access.Draft.Source);
        Assert.Empty(access.Draft.Verification.Scenarios);
        Assert.Empty(access.Draft.Verification.Artifacts);
        Assert.Null(access.Release.Source);
        Assert.Null(access.PreviousRelease.Source);
        Assert.Null(detail.ActiveRelease.Source);
        Assert.Null(detail.PreviousRelease.Source);
        Assert.Equal(64, source.Source.Files.Count);
        Assert.Null(bodyFeature.MaxRequestBodySize);
        Assert.Equal(1, service.ReviseCalls);
        Assert.Equal(1, service.VerificationCalls);
        Assert.Equal(1, service.InstallCalls);
        Assert.Equal(1, service.AccessReviewCalls);
        Assert.Equal(1, service.FeatureCalls);
        Assert.Equal(1, service.SourceCalls);
        Assert.Equal(1, service.GetFeatureDraftCalls);
        var grpc = host.Services.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;
        Assert.Equal(EightMiB, grpc.MaxReceiveMessageSize);
        Assert.Equal(SixteenMiB, grpc.MaxSendMessageSize);
        Assert.NotNull(host.Services.GetRequiredService<DigitalBrainUiEndpoints>());
        Assert.NotNull(host.Services.GetRequiredService<UiGrpcService>());
        var health = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(HealthStatus.Healthy, health.Entries["runtime-ui-transport"].Status);

        var oversized = new ReviseFeatureDraftRequest
        {
            DraftId = "draft-oversized-transport",
            ExpectedRevision = 0,
            IdempotencyId = "source-oversized-transport",
            ReviseSource = new ReviseFeatureSourceInput
            {
                Source = SourceSnapshot(1, EightMiB + 1024)
            }
        };
        Assert.True(oversized.CalculateSize() > EightMiB);

        var rejected = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.ReviseFeatureDraftAsync(oversized));

        Assert.Equal(StatusCode.ResourceExhausted, rejected.StatusCode);
        Assert.Equal(1, service.ReviseCalls);

        var oversizedReply = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.GetFeatureDraftAsync(new GetFeatureDraftRequest
            {
                DraftId = "draft-oversized-response"
            }));

        Assert.Equal(StatusCode.ResourceExhausted, oversizedReply.StatusCode);
        Assert.True(service.OversizedRecoveryReply.CalculateSize() > SixteenMiB);
        Assert.Equal(2, service.GetFeatureDraftCalls);
    }

    private static async Task<IHost> CreateHostAsync(
        TransportUiService service,
        RecordingMaxRequestBodySizeFeature bodyFeature)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer(options => options.BaseAddress = new Uri("https://localhost"))
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DigitalBrain:Runtime:Transport:MaxBodyBytes"] = "6291456"
                    }))
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging();
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton(RuntimeTransportBoundaryOptions.FromConfiguration(context.Configuration));
                    services.AddSingleton(service);
                    UiHostingExtensions.AddUiTransport(
                        services,
                        context.Configuration,
                        context.HostingEnvironment,
                        RuntimeProfile.Development);
                    var tokens = new SessionTokenService(Enumerable.Repeat((byte)31, 32).ToArray(), TimeProvider.System);
                    var conversations = new ConversationStateClient(null!, TimeProvider.System);
                    services.AddSingleton(tokens);
                    services.AddSingleton(new RuntimeSessionAuthority(null!, tokens, TimeProvider.System));
                    services.AddSingleton(new RuntimeSurfaceFeed(null!, TimeProvider.System, tokens));
                    services.AddSingleton(conversations);
                    services.AddSingleton(new McpInoCommandHandler(conversations));
                    services.AddSingleton(new FeatureAuthoringService(null!, null!, null!, null!, TimeProvider.System));
                    services.AddSingleton(new FeatureSuggestionService(null!));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.Use(async (context, next) =>
                    {
                        context.Features.Set<IHttpMaxRequestBodySizeFeature>(bodyFeature);
                        await next();
                    });
                    app.UseMiddleware<RuntimeTransportBoundary>();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<TransportUiService>());
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private static MaximumFeatureFixture MaximumValidFixture(FeatureSourceSnapshot source)
    {
        const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var actorId = new ActorId(MaximumIdentifier(500, 256));
        var draftId = new FeatureDraftId(MaximumIdentifier(501, 128));
        var installationId = new FeatureInstallationId(MaximumIdentifier(502, 256));
        var releaseDigest = new ReleaseDigest(Digest);
        var capabilities = Enumerable.Range(0, 32)
            .Select(index => MaximumIdentifier(index, 256))
            .ToArray();
        var dependencies = Enumerable.Range(0, 64)
            .Select(index => MaximumIdentifier(100 + index, 256))
            .ToArray();
        var subscriptions = Enumerable.Range(0, 64)
            .Select(index => MaximumIdentifier(200 + index, 256))
            .ToArray();
        var grants = capabilities.Select((capabilityId, index) => new FeatureGrantSpec(
            capabilityId,
            int.MaxValue,
            new ProviderConnectionId(MaximumIdentifier(300 + index, 256)),
            MaximumConstraint(capabilityId),
            MaximumIdentifier(400 + index, 64))).ToArray();
        var release = new FeatureReleaseMetadata(
            releaseDigest,
            $"sha256:{Digest}",
            FeatureSourceKind.RuntimeAuthored,
            capabilities,
            dependencies,
            source);
        var createdAt = DateTimeOffset.MinValue;
        var updatedAt = createdAt;
        var verificationEvidence = MaximumPassingEvidence(release);
        var draft = new FeatureDraft(
            draftId,
            new OriginatingRequest(
                MaximumIdentifier(503, 256),
                MaximumIdentifier(504, 256),
                new string('界', 4096)),
            new string('界', 4096),
            "installed",
            MaximumBehavior(),
            source,
            new FeatureVerification(
                releaseDigest,
                verificationEvidence.Total,
                verificationEvidence.Passed,
                verificationEvidence.Failed,
                verificationEvidence.Skipped,
                updatedAt,
                verificationEvidence),
            installationId,
            long.MaxValue,
            createdAt,
            updatedAt);
        var authority = new FeatureAuthoritySnapshot(
            installationId,
            actorId,
            releaseDigest,
            new ReleaseDigest(new string('b', 64)),
            new GrantRevision(2),
            grants,
            null,
            null,
            [],
            false,
            null) with
        {
            ExactRollbackAvailable = true
        };
        var registration = new FeatureInstallationRegistration(installationId, releaseDigest, subscriptions);
        var installed = new InstalledFeatureVersion(draft, release, authority, registration);
        var command = new InstallFeatureVersion(
            draftId,
            long.MaxValue - 1,
            installationId,
            releaseDigest,
            grants,
            subscriptions,
            MaximumIdentifier(505, 256),
            MaximumIdentifier(506, 256));
        var previous = new FeatureReleaseMetadata(
            new ReleaseDigest(new string('b', 64)),
            $"sha256:{new string('b', 64)}",
            FeatureSourceKind.RuntimeAuthored,
            capabilities,
            dependencies,
            source);
        return new MaximumFeatureFixture(actorId, command, installed, previous, long.MaxValue);
    }

    private static FeatureVerificationEvidence MaximumPassingEvidence(FeatureReleaseMetadata release)
    {
        var scenarios = Enumerable.Range(0, 1024).Select(index => new FeatureScenarioEvidence(
            MaximumIdentifier(600 + index, 256),
            new string('n', 512),
            FeatureScenarioOutcome.Passed,
            null,
            70_000)).ToArray();
        var artifacts = Enumerable.Range(0, 32).Select(index => new FeatureVerificationArtifact(
            MaximumAsciiIdentifier($"artifact.{index:D2}.", 256, 'a'),
            MaximumAsciiIdentifier($"media.{index:D2}.", 128, 'm'),
            1_048_576,
            $"sha256:{new string((char)('a' + index % 6), 64)}")).ToArray();
        var evidence = new FeatureVerificationEvidence(
            release.SourceReference,
            scenarios.Length,
            scenarios.Length,
            0,
            0,
            scenarios,
            artifacts);
        var remainingBytes = 2 * 1024 * 1024 - VerificationEvidenceUtf8Bytes(evidence);
        for (var index = 0; remainingBytes > 0; index++)
        {
            var addedBytes = (int)Math.Min(remainingBytes, 1024L);
            scenarios[index] = scenarios[index] with { Name = Utf8ExpandedText(512, addedBytes) };
            remainingBytes -= addedBytes;
        }
        evidence = evidence with { Scenarios = scenarios };
        Assert.Equal(2 * 1024 * 1024, VerificationEvidenceUtf8Bytes(evidence));
        return evidence;
    }

    private static FeatureDraftReply MaximumValidRecoveryReply(MaximumFeatureFixture fixture)
    {
        var installed = fixture.Installed;
        var verification = installed.Draft.Verification!;
        var recovery = new FeatureInstallationRecoverySnapshot(
            true,
            verification,
            installed.Release with { Source = null },
            installed.Registration.InstallationId,
            installed.Authority.ActiveGrants,
            installed.Registration.Subscriptions,
            fixture.PreviousRelease with { Source = null },
            null,
            null,
            true,
            false,
            null);
        return (FeatureDraftReply)InvokeProduction(
            "ProjectDraft",
            [typeof(FeatureDraftId), typeof(FeatureDraftRecoverySnapshot)],
            [installed.Draft.DraftId, new FeatureDraftRecoverySnapshot(installed.Draft, recovery)]);
    }

    private static FeatureDraftReply OversizedRecoveryReply(FeatureDraftReply valid)
    {
        var oversized = valid.Clone();
        var requiredBytes = SixteenMiB + 1024 - oversized.CalculateSize();
        oversized.Draft.Goal += new string('x', requiredBytes);
        Assert.True(oversized.CalculateSize() > SixteenMiB);
        return oversized;
    }

    private static FeatureInstallReply MaximumValidInstallReply(MaximumFeatureFixture fixture) =>
        (FeatureInstallReply)InvokeProduction(
            "ProjectInstallation",
            [typeof(InstallFeatureVersion), typeof(ActorId), typeof(InstalledFeatureVersion)],
            [fixture.InstallCommand, fixture.ActorId, fixture.Installed]);

    private static FeatureReleaseReviewReply MaximumValidVerificationReply(MaximumFeatureFixture fixture)
    {
        var release = fixture.Installed.Release;
        var scenarios = Enumerable.Range(0, 1024).Select(index => new FeatureScenarioEvidence(
            MaximumAsciiIdentifier($"scenario.{index:D4}.", 256, 'i'),
            MaximumAsciiIdentifier($"name.{index:D4}.", 512, 'n'),
            FeatureScenarioOutcome.Failed,
            new string('f', 1024),
            70_000)).ToArray();
        var artifacts = Enumerable.Range(0, 32).Select(index => new FeatureVerificationArtifact(
            MaximumAsciiIdentifier($"artifact.{index:D2}.", 256, 'a'),
            MaximumAsciiIdentifier($"media.{index:D2}.", 128, 'm'),
            1_048_576,
            $"sha256:{new string((char)('a' + index % 6), 64)}")).ToArray();
        var evidence = new FeatureVerificationEvidence(
            release.SourceReference,
            scenarios.Length,
            0,
            scenarios.Length,
            0,
            scenarios,
            artifacts);
        var source = fixture.Installed.Draft.Source;
        var at = DateTimeOffset.UnixEpoch.AddMilliseconds(1);
        var draft = new FeatureDraft(
            fixture.Installed.Draft.DraftId,
            fixture.Installed.Draft.OriginatingRequest,
            fixture.Installed.Draft.Goal,
            "draft",
            fixture.Installed.Draft.Behavior,
            source,
            new FeatureVerification(release.Digest, scenarios.Length, 0, scenarios.Length, 0, at, evidence),
            null,
            long.MaxValue,
            fixture.Installed.Draft.CreatedAt,
            at);
        var command = new VerifyFeatureDraft(draft.DraftId, long.MaxValue - 1, MaximumIdentifier(507, 256));
        return (FeatureReleaseReviewReply)InvokeProduction(
            "ProjectVerification",
            [typeof(VerifyFeatureDraft), typeof(FeatureVerificationReview)],
            [command, new FeatureVerificationReview(draft, null, evidence, at)]);
    }

    private static FeatureAccessReviewReply MaximumValidAccessReviewReply(MaximumFeatureFixture fixture)
    {
        var installed = fixture.Installed;
        var command = new PrepareFeatureAccessReview(
            installed.Draft.DraftId,
            installed.Draft.Revision,
            installed.Registration.InstallationId,
            installed.Release.Digest,
            installed.Authority.ActiveGrants,
            installed.Registration.Subscriptions);
        var review = new FeatureAccessReview(
            new VerifiedFeatureCandidate(installed.Draft, installed.Release),
            installed.Registration.InstallationId,
            installed.Authority.ActiveGrants,
            installed.Registration.Subscriptions,
            fixture.PreviousRelease);
        return (FeatureAccessReviewReply)InvokeProduction(
            "ProjectAccessReview",
            [typeof(PrepareFeatureAccessReview), typeof(FeatureAccessReview)],
            [command, review]);
    }

    private static FeatureReply MaximumValidFeatureReply(MaximumFeatureFixture fixture) =>
        (FeatureReply)InvokeProduction(
            "ProjectFeature",
            [typeof(FeatureDraftId), typeof(ActorId), typeof(InstalledFeatureDetail)],
            [fixture.Installed.Draft.DraftId, fixture.ActorId, MaximumValidDetail(fixture)]);

    private static FeatureReleaseSourceReply MaximumValidSourceReply(MaximumFeatureFixture fixture) =>
        (FeatureReleaseSourceReply)InvokeProduction(
            "ProjectFeatureReleaseSource",
            [
                typeof(FeatureDraftId),
                typeof(FeatureInstallationId),
                typeof(ReleaseDigest),
                typeof(string),
                typeof(ActorId),
                typeof(InstalledFeatureDetail)
            ],
            [
                fixture.Installed.Draft.DraftId,
                fixture.Installed.Registration.InstallationId,
                fixture.Installed.Release.Digest,
                fixture.Installed.Release.SourceReference,
                fixture.ActorId,
                MaximumValidDetail(fixture)
            ]);

    private static InstalledFeatureDetail MaximumValidDetail(MaximumFeatureFixture fixture) => new(
        fixture.Installed.Draft,
        fixture.Installed.Release,
        fixture.PreviousRelease,
        fixture.Installed.Authority,
        fixture.Installed.Registration,
        fixture.Revision);

    private static FeatureBehavior MaximumBehavior()
    {
        var scenarios = Enumerable.Range(0, 32).Select(index => new FeatureScenario(
            MaximumAsciiIdentifier($"scenario.{index:D2}.", 128, 'i'),
            MaximumAsciiIdentifier($"name.{index:D2}.", 256, 'n'),
            new string('g', 555),
            new string('w', 555),
            new string('t', 554))).ToArray();
        Assert.Equal(65_536, scenarios.Sum(scenario =>
            Encoding.UTF8.GetByteCount(scenario.ScenarioId) +
            Encoding.UTF8.GetByteCount(scenario.Name) +
            Encoding.UTF8.GetByteCount(scenario.Given) +
            Encoding.UTF8.GetByteCount(scenario.When) +
            Encoding.UTF8.GetByteCount(scenario.Then)));
        return new FeatureBehavior(scenarios);
    }

    private static FeatureSourceSnapshot ProductionRoundTrip(GrpcFeatureSourceSnapshot source)
    {
        Assert.Equal(64, source.Files.Count);
        Assert.All(source.Files, file => Assert.Equal(240, file.Path.Length));
        Assert.Equal(4 * 1024 * 1024, source.Files.Sum(file => Encoding.UTF8.GetByteCount(file.Content)));
        var mapped = (FeatureSourceSnapshot)InvokeProduction(
            "ToDomain",
            [typeof(GrpcFeatureSourceSnapshot)],
            [source]);
        var projected = (GrpcFeatureSourceSnapshot)InvokeProduction(
            "ToReply",
            [typeof(FeatureSourceSnapshot)],
            [mapped]);
        Assert.Equal(source, projected);
        return mapped;
    }

    private static GrpcFeatureSourceSnapshot MaximumSourceSnapshot() =>
        SourceSnapshot(64, 4 * 1024 * 1024 / 64);

    private static long VerificationEvidenceUtf8Bytes(FeatureVerificationEvidence evidence)
    {
        long bytes = Encoding.UTF8.GetByteCount(evidence.SourceReference);
        foreach (var scenario in evidence.Scenarios)
            bytes += Encoding.UTF8.GetByteCount(scenario.ScenarioId) +
                Encoding.UTF8.GetByteCount(scenario.Name) +
                Encoding.UTF8.GetByteCount(scenario.SafeFailure ?? string.Empty);
        foreach (var artifact in evidence.Artifacts)
            bytes += Encoding.UTF8.GetByteCount(artifact.Name) +
                Encoding.UTF8.GetByteCount(artifact.MediaType) +
                Encoding.UTF8.GetByteCount(artifact.Digest);
        return bytes;
    }

    private static string Utf8ExpandedText(int characters, int addedBytes)
    {
        var threeByteCharacters = addedBytes / 2;
        var twoByteCharacters = addedBytes % 2;
        return new string('界', threeByteCharacters) +
            new string('é', twoByteCharacters) +
            new string('n', characters - threeByteCharacters - twoByteCharacters);
    }

    private static object InvokeProduction(string name, Type[] parameterTypes, object?[] arguments)
    {
        var method = typeof(DigitalBrainUiEndpoints).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            parameterTypes,
            null);
        Assert.NotNull(method);
        return method.Invoke(null, arguments)!;
    }

    private static GrpcFeatureSourceSnapshot SourceSnapshot(int fileCount, int bytesPerFile)
    {
        var implementationProjectPath = MaximumSourcePath(0, ".csproj");
        var scenarioProjectPath = MaximumSourcePath(1, ".csproj");
        var source = new GrpcFeatureSourceSnapshot
        {
            ImplementationProjectPath = implementationProjectPath,
            ScenarioProjectPath = scenarioProjectPath
        };
        for (var index = 0; index < fileCount; index++)
        {
            var path = index switch
            {
                0 => source.ImplementationProjectPath,
                1 => source.ScenarioProjectPath,
                _ => MaximumSourcePath(index, string.Empty)
            };
            source.Files.Add(new GrpcFeatureSourceFile
            {
                Path = path,
                Content = new string((char)('a' + index % 26), bytesPerFile)
            });
        }
        return source;
    }

    private static string MaximumSourcePath(int index, string suffix)
    {
        var discriminator = ((char)(0x4E00 + index)).ToString();
        return discriminator + new string('界', 240 - discriminator.Length - suffix.Length) + suffix;
    }

    private static string MaximumIdentifier(int discriminator, int length) =>
        ((char)(0x4E00 + discriminator)).ToString() + new string('界', length - 1);

    private static string MaximumAsciiIdentifier(string prefix, int length, char fill) =>
        prefix + new string(fill, length - prefix.Length);

    private static string MaximumConstraint(string capabilityId)
    {
        var prefix = $"{{\"allowedToolIds\":[{JsonSerializer.Serialize(capabilityId)}],\"payload\":{{\"scope\":[\"";
        const string Suffix = "\"]}}";
        return prefix + new string('x', 65_536 - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(Suffix)) + Suffix;
    }

    private sealed record MaximumFeatureFixture(
        ActorId ActorId,
        InstallFeatureVersion InstallCommand,
        InstalledFeatureVersion Installed,
        FeatureReleaseMetadata PreviousRelease,
        long Revision);

    private sealed class TransportUiService(
        FeatureDraftReply recoveryReply,
        FeatureDraftReply oversizedRecoveryReply,
        FeatureReleaseReviewReply verificationReply,
        FeatureInstallReply installReply,
        FeatureAccessReviewReply accessReviewReply,
        FeatureReply featureReply,
        FeatureReleaseSourceReply sourceReply) : UiService.DigitalBrainV2UiBase
    {
        public FeatureDraftReply RecoveryReply { get; } = recoveryReply;
        public FeatureDraftReply OversizedRecoveryReply { get; } = oversizedRecoveryReply;
        public FeatureReleaseReviewReply VerificationReply { get; } = verificationReply;
        public FeatureInstallReply InstallReply { get; } = installReply;
        public FeatureAccessReviewReply AccessReviewReply { get; } = accessReviewReply;
        public FeatureReply FeatureReply { get; } = featureReply;
        public FeatureReleaseSourceReply SourceReply { get; } = sourceReply;
        public int ReviseCalls { get; private set; }
        public int VerificationCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public int AccessReviewCalls { get; private set; }
        public int FeatureCalls { get; private set; }
        public int SourceCalls { get; private set; }
        public int GetFeatureDraftCalls { get; private set; }

        public override Task<FeatureDraftReply> GetFeatureDraft(
            GetFeatureDraftRequest request,
            ServerCallContext context)
        {
            GetFeatureDraftCalls++;
            return Task.FromResult(string.Equals(request.DraftId, "draft-oversized-response", StringComparison.Ordinal)
                ? OversizedRecoveryReply
                : RecoveryReply);
        }

        public override Task<FeatureDraftReply> ReviseFeatureDraft(
            ReviseFeatureDraftRequest request,
            ServerCallContext context)
        {
            ReviseCalls++;
            return Task.FromResult(new FeatureDraftReply { Draft = new GrpcFeatureDraft() });
        }

        public override Task<FeatureInstallReply> InstallFeatureVersion(
            InstallFeatureVersionRequest request,
            ServerCallContext context)
        {
            InstallCalls++;
            return Task.FromResult(InstallReply);
        }

        public override Task<FeatureReleaseReviewReply> VerifyFeatureDraft(
            VerifyFeatureDraftRequest request,
            ServerCallContext context)
        {
            VerificationCalls++;
            return Task.FromResult(VerificationReply);
        }

        public override Task<FeatureAccessReviewReply> ReviewFeatureAccess(
            ReviewFeatureAccessRequest request,
            ServerCallContext context)
        {
            AccessReviewCalls++;
            return Task.FromResult(AccessReviewReply);
        }

        public override Task<FeatureReply> GetFeature(GetFeatureRequest request, ServerCallContext context)
        {
            FeatureCalls++;
            return Task.FromResult(FeatureReply);
        }

        public override Task<FeatureReleaseSourceReply> GetFeatureReleaseSource(
            GetFeatureReleaseSourceRequest request,
            ServerCallContext context)
        {
            SourceCalls++;
            return Task.FromResult(SourceReply);
        }
    }

    private sealed class RecordingMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}
