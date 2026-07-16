using System.Security.Cryptography;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace DigitalBrain.Tests.Kernel;

public sealed class RuntimeStateHostingTests
{
    private const string LegacyFeatureHubBlob = """
        {"$id":"1","$type":"DigitalBrain.Kernel.Features.FeatureHubState, DigitalBrain.Kernel","Installations":{"$type":"DigitalBrain.Kernel.Contracts.FeatureInstallationRegistration[], DigitalBrain.Kernel.Contracts","$values":[]},"Revision":0,"FanOuts":{"$type":"DigitalBrain.Kernel.Features.FeatureFanOutState[], DigitalBrain.Kernel","$values":[]},"Releases":{"$type":"DigitalBrain.Kernel.Contracts.FeatureReleaseMetadata[], DigitalBrain.Kernel.Contracts","$values":[]},"Approvals":{"$type":"DigitalBrain.Kernel.Features.FeatureApprovalState[], DigitalBrain.Kernel","$values":[]},"Authorities":{"$type":"DigitalBrain.Kernel.Features.FeatureInstallationAuthorityState[], DigitalBrain.Kernel","$values":[]},"Alerts":{"$type":"DigitalBrain.Kernel.Contracts.FeatureBackpressureAlert[], DigitalBrain.Kernel.Contracts","$values":[]},"Drafts":{"$type":"DigitalBrain.Kernel.Contracts.FeatureDraftProposal[], DigitalBrain.Kernel.Contracts","$values":[{"$id":"2","$type":"DigitalBrain.Kernel.Contracts.FeatureDraftProposal, DigitalBrain.Kernel.Contracts","ProposalId":"proposal-legacy-live","OperationId":"operation-legacy-live","Goal":"Research Acme and create a text file","Status":"draft","CreatedAt":"2026-07-14T17:30:00+00:00"}]}}
        """;

    [Fact]
    public void Hosted_runtime_state_registers_a_purpose_derived_exact_aes256_kek()
    {
        var rawKek = Enumerable.Range(0, 33).Select(static value => (byte)value).ToArray();
        var builder = HostedBuilder(managedIdentity: false, rawKek);

        builder.UseDigitalBrainOrleans();

        using var services = builder.Services.BuildServiceProvider();
        var ring = services.GetRequiredService<IRuntimeStateKeyRing>();
        Assert.Equal(1, ring.ActiveKekVersion);
        Assert.True(ring.TryGetKek(1, out var derived));
        Assert.Equal(32, derived.Length);
        Assert.False(derived.Span.SequenceEqual(rawKek.AsSpan(0, 32)));
        Assert.Same(ring, services.GetRequiredService<RuntimeStateKeyRing>());
        Assert.NotNull(services.GetRequiredService<EncryptedRuntimeStateProtector>());
    }

    [Fact]
    public void Hosted_and_production_configuration_fail_closed_instead_of_selecting_memory_storage()
    {
        var hosted = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        AddStorageConnections(hosted);
        Assert.Throws<InvalidOperationException>(() => hosted.UseDigitalBrainOrleans());

        var production = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        AddKeys(production, RandomNumberGenerator.GetBytes(32));
        var exception = Assert.Throws<InvalidOperationException>(() => production.UseDigitalBrainOrleans());
        Assert.Contains("ConnectionStrings:clustering", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_rejects_oversized_kek_instead_of_deriving_deployment_key_material()
    {
        var production = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        AddStorageConnections(production);
        AddKeys(production, RandomNumberGenerator.GetBytes(33));

        var exception = Assert.Throws<InvalidOperationException>(() => production.UseDigitalBrainOrleans());

        Assert.Contains("exactly 32 bytes in Production", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_storage_and_authenticated_marker_names_are_isolated_by_namespace()
    {
        var mainContainer = RuntimeStateStorageNames.Container("main", "conversations");
        var isolatedContainer = RuntimeStateStorageNames.Container("test-run", "conversations");

        Assert.Equal(mainContainer, RuntimeStateStorageNames.Container(" MAIN ", "conversations"));
        Assert.NotEqual(mainContainer, isolatedContainer);
    }

    [Theory]
    [InlineData(false, "azure-blob-connection-string")]
    [InlineData(true, "azure-blob-managed-identity")]
    public async Task Both_azure_paths_register_dedicated_containers_and_metadata_only_health(
        bool managedIdentity,
        string expectedBackend)
    {
        var builder = HostedBuilder(managedIdentity, RandomNumberGenerator.GetBytes(33));
        builder.UseDigitalBrainOrleans();

        using var services = builder.Services.BuildServiceProvider();
        var storage = services.GetRequiredService<IOptionsMonitor<AzureBlobStorageOptions>>();
        AssertProvider(RuntimeStateStorageProviders.Conversations, "conversations");
        AssertProvider(RuntimeStateStorageProviders.SurfaceFeeds, "surface-feeds");
        AssertProvider(RuntimeStateStorageProviders.Sessions, "sessions");

        var healthOptions = services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = Assert.Single(healthOptions.Registrations,
            static candidate => candidate.Name == "digitalbrain-runtime-state");
        var result = await registration.Factory(services).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(["backendKind", "keyVersion", "namespace", "schemaVersion"],
            result.Data.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(expectedBackend, Assert.IsType<string>(result.Data["backendKind"]));
        Assert.Equal("main", Assert.IsType<string>(result.Data["namespace"]));
        Assert.Equal(RuntimeStateSchemas.Envelope, Assert.IsType<int>(result.Data["schemaVersion"]));
        Assert.Equal(1, Assert.IsType<int>(result.Data["keyVersion"]));

        void AssertProvider(string name, string kind)
        {
            var options = storage.Get(name);
            Assert.Equal(RuntimeStateStorageNames.Container("main", kind), options.ContainerName);
            Assert.NotNull(options.BlobServiceClient);
        }
    }

    [Fact]
    public void Default_azure_storage_reads_the_exact_legacy_feature_draft_blob_shape()
    {
        var builder = HostedBuilder(managedIdentity: false, RandomNumberGenerator.GetBytes(33));
        builder.UseDigitalBrainOrleans();

        using var services = builder.Services.BuildServiceProvider();
        var storage = services.GetRequiredService<IOptionsMonitor<AzureBlobStorageOptions>>().Get("Default");
        var state = storage.GrainStorageSerializer.Deserialize<FeatureHubState>(BinaryData.FromString(LegacyFeatureHubBlob));
        var draft = Assert.Single(state.Drafts ?? []);

        Assert.Equal(new FeatureDraftId("proposal-legacy-live"), draft.DraftId);
        Assert.Equal("operation-legacy-live", draft.OriginatingRequest.OperationId);
        Assert.Equal(FeatureDraft.LegacyMissingConversationId, draft.OriginatingRequest.ConversationId);
        Assert.Equal("Research Acme and create a text file", draft.Goal);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 17, 30, 0, TimeSpan.Zero), draft.CreatedAt);
        Assert.True(state.RequiresStorageRewrite);
        var rewritten = storage.GrainStorageSerializer.Serialize(state with { RequiresStorageRewrite = false }).ToString();
        Assert.DoesNotContain("FeatureDraftProposal", rewritten, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain.Kernel.Contracts.FeatureDraft, DigitalBrain.Kernel.Contracts", rewritten, StringComparison.Ordinal);
    }

    private static HostApplicationBuilder HostedBuilder(bool managedIdentity, byte[] rawKek)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        if (managedIdentity)
            builder.Configuration["DigitalBrain:Storage:AccountName"] = "digitalbrainstate";
        else
            AddStorageConnections(builder);
        AddKeys(builder, rawKek);
        return builder;
    }

    private static void AddStorageConnections(HostApplicationBuilder builder)
    {
        builder.Configuration["ConnectionStrings:clustering"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:grainstate"] = "UseDevelopmentStorage=true";
    }

    private static void AddKeys(HostApplicationBuilder builder, byte[] rawKek)
    {
        builder.Configuration["DigitalBrain:Runtime:State:ActiveKekVersion"] = "1";
        builder.Configuration["DigitalBrain:Runtime:State:Keks:1"] = Convert.ToBase64String(rawKek);
        builder.Configuration["DigitalBrain:Runtime:State:SigningKey"] =
            Convert.ToBase64String(Enumerable.Repeat((byte)0xD7, 33).ToArray());
    }
}
