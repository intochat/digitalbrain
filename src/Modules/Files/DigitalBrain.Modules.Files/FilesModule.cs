using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Files;

[ModuleId("files")]
public sealed class FilesModule : IModule, IHttpModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton<IAssetBlobStore, AzureAssetBlobStore>();
        silo.Services.TryAddSingleton<WorkspaceFileStore>();
        silo.Services.TryAddSingleton<FileSurfaces>();
        silo.Services.TryAddSingleton<ImageSaveCoordinator>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        FilesEndpoints.Map(endpoints);
    }
}
