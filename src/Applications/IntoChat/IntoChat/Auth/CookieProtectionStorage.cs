using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace IntoChat;

public static class CookieProtectionStorage
{
    public const string ContainerName = "intochat-protection-v1";

    internal static IServiceCollection AddCookieProtection(this IServiceCollection services)
    {
        services.AddSingleton(provider => provider
            .GetRequiredKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState)
            .GetBlobContainerClient(ContainerName));
        services.AddHostedService<ContainerStartup>();
        services.AddDataProtection().SetApplicationName("IntoChat.v1")
            .PersistKeysToAzureBlobStorage(provider => provider.GetRequiredService<BlobContainerClient>().GetBlobClient("keys.xml"));
        return services;
    }

    // DeploymentKit substitutes storage endpoints but does not execute Aspire's container provisioning.
    private sealed class ContainerStartup(BlobContainerClient container) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
