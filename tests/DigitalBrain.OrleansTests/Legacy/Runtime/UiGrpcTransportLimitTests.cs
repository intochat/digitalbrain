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
using FeatureSuggestionService = McpProject::DigitalBrain.Mcp.FeatureSuggestionService;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using RuntimeTransportBoundary = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundary;
using RuntimeTransportBoundaryOptions = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundaryOptions;
using UiHostingExtensions = McpProject::DigitalBrain.Mcp.UiHostingExtensions;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;
using FeatureDraftReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftReply;
using FeatureInstallReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureInstallReply;
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
using InstallFeatureVersionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.InstallFeatureVersionRequest;
using ReviseFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureDraftRequest;
using ReviseFeatureSourceInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureSourceInput;
using UiService = McpProject::DigitalBrain.V2.Ui.Grpc.DigitalBrainV2Ui;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiGrpcTransportLimitTests
{
    private const int EightMiB = 8 * 1024 * 1024;

    [Fact]
    public async Task Actual_gRPC_pipeline_transports_production_projected_valid_maxima_and_rejects_above_eight_MiB_before_handler()
    {
        var validSource = MaximumSourceSnapshot();
        var domainSource = ProductionRoundTrip(validSource);
        var service = new TransportUiService(MaximumValidInstallReply(domainSource));
        var bodyFeature = new RecordingMaxRequestBodySizeFeature();
        using var host = await CreateHostAsync(service, bodyFeature);
        var server = host.GetTestServer();
        using var channel = GrpcChannel.ForAddress(server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = server.CreateHandler(),
            MaxReceiveMessageSize = EightMiB,
            MaxSendMessageSize = 16 * 1024 * 1024
        });
        var client = new UiService.DigitalBrainV2UiClient(channel);
        var valid = new ReviseFeatureDraftRequest
        {
            DraftId = "draft-valid-four-mib",
            ExpectedRevision = 0,
            IdempotencyId = "source-valid-four-mib",
            ReviseSource = new ReviseFeatureSourceInput { Source = validSource.Clone() }
        };
        var maximumReplySize = service.InstallReply.CalculateSize();

        var revised = await client.ReviseFeatureDraftAsync(valid);
        var installed = await client.InstallFeatureVersionAsync(new InstallFeatureVersionRequest());

        Assert.NotNull(revised);
        Assert.Equal(maximumReplySize, installed.CalculateSize());
        Assert.True(valid.CalculateSize() > 4 * 1024 * 1024);
        Assert.True(valid.CalculateSize() < EightMiB);
        Assert.True(maximumReplySize > 4 * 1024 * 1024);
        Assert.True(maximumReplySize < EightMiB);
        Assert.Null(bodyFeature.MaxRequestBodySize);
        Assert.Equal(1, service.ReviseCalls);
        Assert.Equal(1, service.InstallCalls);
        var grpc = host.Services.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;
        Assert.Equal(EightMiB, grpc.MaxReceiveMessageSize);
        Assert.Equal(EightMiB, grpc.MaxSendMessageSize);
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

    private static FeatureInstallReply MaximumValidInstallReply(FeatureSourceSnapshot source)
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
            dependencies);
        var createdAt = DateTimeOffset.MinValue;
        var updatedAt = createdAt;
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
            new FeatureVerification(releaseDigest, 32, 32, 0, 0, updatedAt),
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
            null);
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
        return (FeatureInstallReply)InvokeProduction(
            "ProjectInstallation",
            [typeof(InstallFeatureVersion), typeof(ActorId), typeof(InstalledFeatureVersion)],
            [command, actorId, installed]);
    }

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

    private sealed class TransportUiService(FeatureInstallReply installReply) : UiService.DigitalBrainV2UiBase
    {
        public FeatureInstallReply InstallReply { get; } = installReply;
        public int ReviseCalls { get; private set; }
        public int InstallCalls { get; private set; }

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
    }

    private sealed class RecordingMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}
