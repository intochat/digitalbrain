using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Features;

internal interface IFeaturePublicationVerifier
{
    Task VerifyAsync(
        BrainOwnerId ownerId,
        FeaturePublicationTicket ticket,
        FeaturePublicationReceipt receipt,
        CancellationToken cancellationToken = default);
}

internal sealed class BlobFeaturePublicationVerifier(
    [FromKeyedServices("features")] BlobServiceClient blobs) : IFeaturePublicationVerifier
{
    private const string ContainerName = "feature-releases";
    private const int MaximumManifestBytes = 65_536;
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(ContainerName);

    public async Task VerifyAsync(
        BrainOwnerId ownerId,
        FeaturePublicationTicket ticket,
        FeaturePublicationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(receipt);
        if (ticket.InstallationId != receipt.InstallationId ||
            ticket.PublicationFence != receipt.PublicationFence ||
            !string.Equals(ticket.AuthorityDigest, receipt.AuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(ticket.AccessDigest, receipt.AccessDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The Feature publication receipt does not match the active publication ticket.",
                FeatureCommandRejectionReason.Precondition);
        var expected = FeaturePublicationManifestCodec.Serialize(ownerId, ticket);
        var expectedDigest = Convert.ToHexStringLower(SHA256.HashData(expected));
        if (!string.Equals(expectedDigest, receipt.ManifestDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The Feature publication receipt has another manifest digest.",
                FeatureCommandRejectionReason.Precondition);
        var blob = container.GetBlobClient(FeaturePublicationManifestCodec.Path(ownerId, ticket.InstallationId));
        BlobProperties properties;
        try
        {
            properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new FeatureConcurrencyException(
                "The exact active Feature publication does not exist.",
                FeatureCommandRejectionReason.Precondition);
        }
        if (properties.ContentLength < 2 || properties.ContentLength > MaximumManifestBytes)
            throw new FeatureConcurrencyException(
                "The active Feature publication manifest is invalid.",
                FeatureCommandRejectionReason.Precondition);
        var options = new BlobDownloadOptions
        {
            Conditions = new BlobRequestConditions { IfMatch = properties.ETag }
        };
        byte[] actual;
        try
        {
            using var download = (await blob.DownloadStreamingAsync(options, cancellationToken).ConfigureAwait(false)).Value;
            using var output = new MemoryStream(checked((int)properties.ContentLength));
            var buffer = new byte[4096];
            while (true)
            {
                var read = await download.Content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > MaximumManifestBytes)
                    throw new FeatureConcurrencyException(
                        "The active Feature publication manifest is invalid.",
                        FeatureCommandRejectionReason.Precondition);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            actual = output.ToArray();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new FeatureConcurrencyException(
                "The exact active Feature publication does not exist.",
                FeatureCommandRejectionReason.Precondition);
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            throw new FeatureConcurrencyException("The active Feature publication changed during verification.");
        }
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new FeatureConcurrencyException(
                "The active Feature publication does not match the exact authority ticket.",
                FeatureCommandRejectionReason.Precondition);
    }
}
