using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Kernel;

// Regression coverage for the task-24 "pack config silently ephemeral in production" bug. Mirrors
// AzureClientHealthCheckRegistrationTests.cs's approach: replicate Program.cs's isAspireHosted Azure client
// wiring against a bare IHostApplicationBuilder (no Kestrel/Orleans/real Azurite needed - the bug is a pure
// DI-resolution failure, not something that needs a live storage account to reproduce).
public class PackConfigBackingStoreSelectionTests
{
    [Fact]
    public void AspireHosted_WithKeyedGrainStorageBlobClient_StillSelectsAzureBackingStore()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:grainstate"] = "UseDevelopmentStorage=true";

        // Mirrors Program.cs's isAspireHosted branch: a keyed BlobServiceClient registration for grain
        // storage exists alongside the unkeyed one used for health-check wiring. Per Aspire's
        // AzureComponent.AddClient, the keyed call's null-sentinel shadows any *other* unkeyed
        // BlobServiceClient registration in the same container, so sp.GetService<BlobServiceClient>()
        // (unkeyed) always returns null here - exactly like the /health 500 bug, just a different consumer.
        // Neither registration is ever resolved in this test, so no real Azurite connection is attempted.
        builder.AddKeyedAzureBlobServiceClient("grainstate");
        builder.AddAzureBlobServiceClient("grainstate", settings => settings.DisableHealthChecks = true);

        // Mirrors Program.cs: packConfigBlobs is constructed explicitly (not resolved from DI) and handed
        // straight to AddPackConfigStore. A no-network fake stands in for the real BlobServiceClient built
        // from the connection string, since AddPackConfigStore synchronously calls CreateIfNotExists on a
        // "pack-config" container (for the DataProtection key ring) as part of registration - a real client
        // would require a reachable storage account/Azurite just to construct the DI container.
        var packConfigBlobs = new NoNetworkBlobServiceClient();
        builder.Services.AddSingleton<IRuntimeStateKeyRing>(new StableTestRuntimeStateKeyRing());
        builder.Services.AddPackConfigStore(packConfigBlobs);

        using var host = builder.Build();

        // Before the fix, AddPackConfigStore's IPackConfigBackingStore factory resolved
        // sp.GetService<BlobServiceClient>() (unkeyed) instead of reusing the passed-in packConfigBlobs
        // parameter, which returns null here due to the shadowing above - silently falling back to
        // InMemoryPackConfigBackingStore even though a real client was supplied and Aspire-hosted mode was
        // active. That meant pack config (Salesforce/Google/Telegram OAuth tokens, etc.) was never actually
        // durable or shared across the kernel's HA replicas in production.
        var backingStore = host.Services.GetRequiredService<IPackConfigBackingStore>();

        Assert.IsType<AzureBlobPackConfigBackingStore>(backingStore);
    }

    [Fact]
    public void AspireHosted_WithoutStableIdentifierKey_FailsClosed()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPackConfigStore(new NoNetworkBlobServiceClient());

        using var host = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredService<IPackConfigBackingStore>());

        Assert.Contains(nameof(IRuntimeStateKeyRing), exception.Message);
    }

    // Azure SDK clients are designed for subclass-based test doubles: a protected parameterless constructor
    // plus virtual members, no mocking library required (see Azure.Core's README "Mocking" guidance). These
    // no-op just enough of the surface that AddPackConfigStore touches at registration time (creating the
    // "pack-config" container for the DataProtection key ring) to avoid a real network call.
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
