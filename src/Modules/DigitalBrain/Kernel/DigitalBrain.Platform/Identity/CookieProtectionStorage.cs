using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Identity.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Identity;

internal static class CookieProtectionStorage
{
    internal static IServiceCollection AddCookieProtection(this IServiceCollection services)
    {
        services.AddSingleton(provider => provider
            .GetRequiredKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState)
            .GetBlobContainerClient(provider.GetRequiredService<IOptions<IdentityHostOptions>>().Value.ProtectionContainerName));
        services.AddHostedService<ContainerStartup>();
        services.AddDataProtection()
            .PersistKeysToAzureBlobStorage(provider => provider.GetRequiredService<BlobContainerClient>().GetBlobClient("keys.xml"));
        services.AddOptions<DataProtectionOptions>().Configure<IOptions<IdentityHostOptions>>((protection, host) =>
            protection.ApplicationDiscriminator = host.Value.ProtectionApplicationName);
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
