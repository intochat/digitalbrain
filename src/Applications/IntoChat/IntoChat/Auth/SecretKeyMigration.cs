using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Sdk.Secrets;

namespace IntoChat;

// Run after the silo starts, while the original profile can still unwrap legacy DPAPI keys.
// Inventory uses the configured Orleans Blob naming convention; no secret value leaves its grain.
internal sealed class SecretKeyMigration(
    [FromKeyedServices(DigitalBrainNames.GrainState)] BlobServiceClient blobs,
    IGrainFactory grains, ILogger<SecretKeyMigration> logger) : IHostedLifecycleService
{
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        const string prefix = "vault-vault/";
        const string suffix = ".json";
        var count = 0;
        await foreach (var blob in blobs.GetBlobContainerClient("digitalbrain-v2-state").GetBlobsAsync(
            Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, cancellationToken))
        {
            if (!blob.Name.EndsWith(suffix, StringComparison.Ordinal)) { continue; }
            var owner = blob.Name[prefix.Length..^suffix.Length];
            if (!await grains.GetGrain<ISecretKeyMigration>(owner).EnsurePortable().WaitAsync(cancellationToken))
            { throw new InvalidOperationException("A legacy secret key could not be migrated. Start this version under the original Windows profile before moving hosts."); }
            count++;
        }
        logger.LogInformation("Verified portable wrapping for {Count} persisted secret vaults.", count);
    }
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
