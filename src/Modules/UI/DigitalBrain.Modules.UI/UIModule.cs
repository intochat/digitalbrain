using Azure.Storage.Blobs;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.UI;

public sealed class UIModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Resolved lazily so the choice does not depend on whether the host registered its blob
        // client before or after this module.
        builder.Services.TryAddSingleton<IKitImageStore>(services =>
            CreateStore<IKitImageStore>(services, blobs => new BlobKitImageStore(blobs), () => new MemoryKitImageStore()));
        builder.Services.TryAddSingleton<ITurnContextBlobStore>(services =>
            CreateStore<ITurnContextBlobStore>(services, blobs => new BlobTurnContextStore(blobs), () => new MemoryTurnContextStore()));

        builder.AddStartupTask((services, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A silo without the AI module must still start.
            if (services.GetService<NativeTools>() is not { } nativeTools)
            {
                return Task.CompletedTask;
            }

            // GetService (nullable) is the honesty gate: generate_image only appears once an
            // IImageGeneration provider is actually configured.
            var imageGeneration = services.GetService<IImageGeneration>();
            var tools = new KitTools(services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<INeuronInvoker>(), imageGeneration,
                services.GetRequiredService<IKitImageStore>());
            foreach (var tool in tools.Create())
            {
                nativeTools.Add(tool.Name, tool);
            }

            return Task.CompletedTask;
        });
    }

    private static T CreateStore<T>(IServiceProvider services, Func<BlobServiceClient, T> blobStore, Func<T> memoryStore)
        => services.GetKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState) is { } blobs
            ? blobStore(blobs)
            : memoryStore();
}
