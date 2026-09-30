using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.CSharp.Tests;

public sealed class CSharpRouteFacts
{
    [Fact]
    public void CSharpAuthoringIsServedOnlyUnderTheBrainRoute()
    {
        var (_, endpoints) = MapAuthoringRoutes(developerMode: true);
        var routes = endpoints.Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/csharp/", routes);
        Assert.Contains("/brains/{brainId}/csharp/{id}/share", routes);
        Assert.All(routes, route => Assert.StartsWith("/brains/{brainId}/csharp", route));
    }

    [Fact]
    public async Task CSharpAuthoringIsNotFoundWhenDeveloperModeIsOff()
    {
        var (app, endpoints) = MapAuthoringRoutes(developerMode: false);
        var list = endpoints.Single(route => route.RoutePattern.RawText == "/brains/{brainId}/csharp/"
            && route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.RouteValues["brainId"] = "brain-1";

        await list.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static (WebApplication App, RouteEndpoint[] Endpoints) MapAuthoringRoutes(bool developerMode)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOptions<CSharpAuthoringOptions>().Configure(options => options.DeveloperMode = developerMode);
        builder.Services.AddSingleton(new ScopedCSharpTools(null!, null!, null, "scope", canRun: false));
        builder.Services.AddSingleton(new CSharpSharing(null!, null!));
        var app = builder.Build();
        CSharpAuthoringEndpoints.Map(app);
        return (app, ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray());
    }
}
