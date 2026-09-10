using Azure.Storage.Blobs;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
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

        // Registered through the AI module's contributor seam; each tool is built on first use.
        builder.Services.AddNativeTool("render_chart", services => KitToolNamed(services, "render_chart"));
        builder.Services.AddNativeTool("show_graph", services => KitToolNamed(services, "show_graph"));
        // generate_image appears only once an image model is configured, the same gate AIClients uses.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["DigitalBrain:AI:Default:Image"]))
        {
            builder.Services.AddNativeTool("generate_image", services => KitToolNamed(services, "generate_image"));
        }
    }

    private static AIFunction KitToolNamed(IServiceProvider services, string name)
    {
        var tools = new KitTools(services.GetRequiredService<IGrainFactory>(),
            services.GetRequiredService<INeuronInvoker>(), services.GetService<IImageGeneration>(),
            services.GetRequiredService<IKitImageStore>());
        return tools.Create().Single(tool => tool.Name == name);
    }

    private static T CreateStore<T>(IServiceProvider services, Func<BlobServiceClient, T> blobStore, Func<T> memoryStore)
        => services.GetKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState) is { } blobs
            ? blobStore(blobs)
            : memoryStore();
}
