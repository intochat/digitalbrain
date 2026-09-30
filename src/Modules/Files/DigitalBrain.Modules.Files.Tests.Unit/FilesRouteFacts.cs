using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Files.Tests;

public sealed class FilesRouteFacts
{
    [Fact]
    public void FilesAreServedOnlyUnderTheBrainRoute()
    {
        var routes = MapFilesRoutes().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/apps/files", routes);
        Assert.All(routes, route => Assert.StartsWith("/brains/{brainId}/apps/", route));
    }

    private static RouteEndpoint[] MapFilesRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        foreach (var handlerService in new[] { typeof(IDigitalBrain), typeof(WorkspaceFileStore), typeof(FileSurfaces), typeof(ImageSaveCoordinator) })
        {
            builder.Services.AddSingleton(handlerService, _ => null!);
        }
        var app = builder.Build();
        FilesEndpoints.Map(app);
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
    }
}
