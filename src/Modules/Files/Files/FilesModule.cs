using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Files;

[ModuleConfiguration(typeof(FilesConfigurationContract))]
public sealed class FilesModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(FilesModule));

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
