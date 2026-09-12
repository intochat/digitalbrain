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
        builder.Services.TryAddSingleton<TableService>();
        builder.Services.AddSingleton<ITableSource>(new TableSource("table-", UIVocabulary.TableType));
        // Resolved lazily so the choice does not depend on whether the host registered its blob
        // client before or after this module.
        builder.Services.TryAddSingleton<IUiImageStore>(services =>
            CreateStore<IUiImageStore>(services, blobs => new BlobUiImageStore(blobs), () => new MemoryUiImageStore()));
        builder.Services.TryAddSingleton<ITurnContextBlobStore>(services =>
            CreateStore<ITurnContextBlobStore>(services, blobs => new BlobTurnContextStore(blobs), () => new MemoryTurnContextStore()));

        // Registered through the AI module's contributor seam; each tool is built on first use.
        // The table tools are the same ones the workspace agent gets directly, so a uichat agent
        // instructed with them can refine a table the way that agent does.
        foreach (var tool in new[] { "create_table", "read_table", "update_table_view", "list_tables" })
        {
            builder.Services.AddNativeTool(tool, services => TableToolNamed(services, tool));
        }

        builder.Services.AddNativeTool("render_chart", services => UiToolNamed(services, "render_chart"));
        builder.Services.AddNativeTool("show_graph", services => UiToolNamed(services, "show_graph"));
        // generate_image appears only once an image model is configured, the same gate AIClients uses.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["DigitalBrain:AI:Default:Image"]))
        {
            builder.Services.AddNativeTool("generate_image", services => UiToolNamed(services, "generate_image"));
        }
    }

    private static AIFunction TableToolNamed(IServiceProvider services, string name)
        => new TableAgentTools(services.GetRequiredService<TableService>()).Create().OfType<AIFunction>().Single(tool => tool.Name == name);

    private static AIFunction UiToolNamed(IServiceProvider services, string name)
    {
        var tools = new UiTools(services.GetRequiredService<IGrainFactory>(),
            services.GetRequiredService<INeuronInvoker>(), services.GetService<IImageGeneration>(),
            services.GetRequiredService<IUiImageStore>());
        return tools.Create().Single(tool => tool.Name == name);
    }

    private static T CreateStore<T>(IServiceProvider services, Func<BlobServiceClient, T> blobStore, Func<T> memoryStore)
        => services.GetKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState) is { } blobs
            ? blobStore(blobs)
            : memoryStore();
}
