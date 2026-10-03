using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Contracts;
using DigitalBrain.Platform.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace DigitalBrain.Platform.Identity.Directory;

// Azure Blob grain keys do not contain Orleans ServiceId. Bind the store explicitly without
// renaming any legacy grain blobs. Cluster membership and display names are intentionally absent.
internal sealed class DeploymentStorageIdentity(
    IOptions<ClusterOptions> cluster, IKeyWrapper keys, IOptions<IdentityMigrationOptions> migration,
    [FromKeyedServices(DigitalBrainNames.GrainState)] BlobServiceClient? storage = null)
{
    private const string MarkerName = ".digitalbrain-deployment.json";
    private static readonly byte[] Proof = "DigitalBrain deployment key binding v1"u8.ToArray();

    public async Task ValidateAsync(CancellationToken ct)
    {
        if (storage is null) { return; } // In-memory unit/development hosts have no durable store.
        var container = storage.GetBlobContainerClient(DigitalBrainNames.GrainStateContainer);
        if (!await container.ExistsAsync(ct)) { return; }
        var marker = await ReadAsync(container, ct);
        if (marker is not null) { Validate(marker, cluster.Value.ServiceId, keys); }
        else if (!migration.Value.Maintenance)
        {
            await foreach (var page in container.GetBlobsAsync(cancellationToken: ct).AsPages(pageSizeHint: 1))
            {
                if (page.Values.Count > 0)
                { throw new OptionsValidationException("DeploymentStorage", typeof(ClusterOptions), ["Existing state requires explicit maintenance adoption before normal startup."]); }
            }
        }
    }

    public async Task BindAsync(CancellationToken ct)
    {
        if (storage is null) { return; }
        await ValidateAsync(ct);
        var container = storage.GetBlobContainerClient(DigitalBrainNames.GrainStateContainer);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var current = await ReadAsync(container, ct);
        if (current is not null) { Validate(current, cluster.Value.ServiceId, keys); return; }
        var binding = new Binding(1, cluster.Value.ServiceId, keys.Wrap(Proof));
        try
        {
            await container.GetBlobClient(MarkerName).UploadAsync(BinaryData.FromObjectAsJson(binding),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, ct);
        }
        catch (RequestFailedException error) when (error.Status is 409 or 412)
        {
            var winner = await ReadAsync(container, ct) ?? throw new InvalidOperationException("Deployment binding disappeared during initialization.");
            Validate(winner, cluster.Value.ServiceId, keys);
        }
    }

    private static async Task<Binding?> ReadAsync(BlobContainerClient container, CancellationToken ct)
    {
        try
        {
            return (await container.GetBlobClient(MarkerName).DownloadContentAsync(ct)).Value.Content.ToObjectFromJson<Binding>()
                ?? throw new InvalidOperationException("Invalid deployment binding.");
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
    }

    internal static void Validate(Binding binding, string serviceId, IKeyWrapper keys)
    {
        if (binding.Version != 1) { throw new InvalidOperationException("Unsupported deployment binding version."); }
        if (binding.ServiceId != serviceId)
        { throw new OptionsValidationException(nameof(ClusterOptions.ServiceId), typeof(ClusterOptions), ["The configured service ID does not match this persistent deployment."]); }
        try
        {
            if (!keys.Unwrap(binding.KeyProof).SequenceEqual(Proof)) { throw new CryptographicException("Invalid deployment key proof."); }
        }
        catch (CryptographicException)
        { throw new OptionsValidationException(nameof(MasterKeyOptions.MasterKey), typeof(MasterKeyOptions), ["The configured master key does not match this persistent deployment."]); }
    }

    internal sealed record Binding(int Version, string ServiceId, string KeyProof);
}
