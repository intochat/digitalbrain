using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Kernel;

public class IntegrationConfigBackingStoreSelectionTests
{
    [Fact]
    public void AspireHosted_WithKeyedGrainStorageBlobClient_StillSelectsAzureBackingStore()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:grainstate"] = "UseDevelopmentStorage=true";

        builder.AddKeyedAzureBlobServiceClient("grainstate");
        builder.AddAzureBlobServiceClient("grainstate", settings => settings.DisableHealthChecks = true);

        var integrationConfigBlobs = new NoNetworkBlobServiceClient();
        builder.Services.AddSingleton<IRuntimeStateKeyRing>(new StableTestRuntimeStateKeyRing());
        builder.Services.AddIntegrationConfigStore(integrationConfigBlobs);

        using var host = builder.Build();

        var backingStore = host.Services.GetRequiredService<IIntegrationConfigBackingStore>();

        Assert.IsType<AzureBlobIntegrationConfigBackingStore>(backingStore);
    }

    [Fact]
    public void AspireHosted_WithoutStableIdentifierKey_FailsClosed()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddIntegrationConfigStore(new NoNetworkBlobServiceClient());

        using var host = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredService<IIntegrationConfigBackingStore>());

        Assert.Contains(nameof(IRuntimeStateKeyRing), exception.Message);
    }

    private sealed class NoNetworkBlobServiceClient : BlobServiceClient
    {
        private readonly NoNetworkBlobContainerClient _container = new();

        public override BlobContainerClient GetBlobContainerClient(string blobContainerName) => _container;
    }

    private sealed class NoNetworkBlobContainerClient : BlobContainerClient
    {
        public override Response<BlobContainerInfo> CreateIfNotExists(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
            => null!;

        public override BlobClient GetBlobClient(string blobName) => new NoNetworkBlobClient();
    }

    private sealed class NoNetworkBlobClient : BlobClient;

    private sealed class StableTestRuntimeStateKeyRing : IRuntimeStateKeyRing
    {
        private readonly byte[] _signingKey = Enumerable.Repeat((byte)0x5a, 32).ToArray();

        public int ActiveKekVersion => 1;
        public ReadOnlyMemory<byte> SigningKey => _signingKey;

        public bool TryGetKek(int version, out ReadOnlyMemory<byte> key)
        {
            key = default;
            return false;
        }
    }
}
