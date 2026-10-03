using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Files.Tests.Unit;

public sealed class FilesRouteFacts
{
    [Fact]
    public void FilesAreServedOnlyUnderTheBrainRoute()
    {
        var routes = MapFilesRoutes().Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "GET /brains/{brainId}/apps/assets/{assetId}",
                "GET /brains/{brainId}/apps/files",
                "GET /brains/{brainId}/apps/images-ui",
                "GET /brains/{brainId}/apps/images/{documentId}",
                "POST /brains/{brainId}/apps/files/open",
                "POST /brains/{brainId}/apps/files/upload",
                "POST /brains/{brainId}/apps/images/{documentId}/edit",
                "POST /brains/{brainId}/apps/images/{documentId}/prepare-save",
                "POST /brains/{brainId}/apps/images/{documentId}/save/{operationId}",
            ],
            routes);
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
