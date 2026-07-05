using Azure.Core;
using Azure.Storage.Blobs;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Sync;

namespace DigitalBrain.Kernel.Kernel;

public static class KernelServices
{
    // Registers checkpoint encryption. The key comes from ICheckpointKeyProvider (config today, Key Vault later).
    // AES-GCM when a key is present; in Production a missing key fails fast; in dev it falls back to PassThrough
    // with a loud warning so the absence of encryption is never silent.
    public static IServiceCollection AddKernelSecurity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var keyProvider = new ConfigCheckpointKeyProvider(configuration);
        services.AddSingleton<ICheckpointKeyProvider>(keyProvider);
        var key = keyProvider.GetKey();

        if (key is not null)
        {
            services.AddSingleton<INeuronStateProtector>(new AesNeuronStateProtector(key));
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "DigitalBrain:Checkpoint:Key is required in Production (checkpoints must be encrypted). " +
                "Supply it from Key Vault via an ICheckpointKeyProvider.");
        }
        else
        {
            services.AddSingleton<INeuronStateProtector>(sp =>
            {
                sp.GetService<ILoggerFactory>()?.CreateLogger("KernelSecurity").LogWarning(
                    "No DigitalBrain:Checkpoint:Key configured — checkpoints are NOT encrypted (PassThrough). " +
                    "Configure a key (Key Vault) before production.");
                return new PassThroughNeuronStateProtector();
            });
        }

        services.AddSingleton<CheckpointProtector>();
        return services;
    }

    // Registers the "sync" BlobContainerClient (Task 20's provisioned container, ConnectionStrings__sync locally/
    // Aspire-hosted) and CheckpointBackupTrigger. Mirrors the useManagedIdentity branch Program.cs already uses
    // for clustering/grainstate/journal/packConfigBlobs (Task 18): a real Azure storage account
    // (DigitalBrain:Storage:AccountName set — only true on the ACA deploy) authenticates via TokenCredential;
    // everywhere else (Aspire/local Azurite, tests) falls back to the injected connection string. Keeping "sync"
    // on this same branch means it isn't the one storage consumer left behind once shared-key access is
    // eventually disabled (Task 18/19's deferred "Step 5").
    public static IServiceCollection AddCheckpointSync(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useManagedIdentity,
        TokenCredential? storageCredential,
        Uri? storageBlobServiceUri)
    {
        services.AddSingleton(_ =>
        {
            var blobServiceClient = useManagedIdentity
                ? new BlobServiceClient(storageBlobServiceUri!, storageCredential!)
                : new BlobServiceClient(configuration.GetConnectionString("sync") ?? throw new InvalidOperationException(
                    "ConnectionStrings:sync is required outside managed-identity mode (Aspire/local inject it " +
                    "via WithReference(ctx.SyncBlobs); the ACA deploy sets DigitalBrain:Storage:AccountName " +
                    "instead, which routes here through the useManagedIdentity branch)."));
            return blobServiceClient.GetBlobContainerClient("sync");
        });
        services.AddSingleton<CheckpointBackupTrigger>();
        return services;
    }
}
