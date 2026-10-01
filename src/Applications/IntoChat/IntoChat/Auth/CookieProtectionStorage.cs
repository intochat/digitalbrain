using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace IntoChat;

public static class CookieProtectionStorage
{
    public const string ContainerName = "intochat-protection-v1";

    internal static IServiceCollection AddCookieProtection(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("IntoChat.v1")
            .PersistKeysToAzureBlobStorage(provider => provider
                .GetRequiredKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState)
                .GetBlobContainerClient(ContainerName).GetBlobClient("keys.xml"));
        return services;
    }
}
