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
    private sealed class ContainerStartup(BlobContainerClient container) : IHostedLifecycleService
    {
        private Task? _creation;
        public Task StartingAsync(CancellationToken cancellationToken)
            => _creation ??= container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
