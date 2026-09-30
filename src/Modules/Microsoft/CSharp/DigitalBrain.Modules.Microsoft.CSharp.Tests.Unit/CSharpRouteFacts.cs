using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.CSharp.Tests;

public sealed class CSharpRouteFacts
{
    [Fact]
    public void CSharpAuthoringIsServedOnlyUnderTheBrainRoute()
    {
        var (_, endpoints) = MapAuthoringRoutes();
        var routes = endpoints.Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/csharp/", routes);
        Assert.Contains("/brains/{brainId}/csharp/{id}/share", routes);
        Assert.All(routes, route => Assert.StartsWith("/brains/{brainId}/csharp", route));
    }

    [Fact]
    public async Task CSharpAuthoringIsNotFoundWhenDeveloperModeIsOff()
    {
        Assert.Equal(StatusCodes.Status404NotFound, await ListStatus(("DigitalBrain:CSharpAuthoring:DeveloperMode", "false")));
    }

    [Fact]
    public async Task CSharpAuthoringIsNotFoundWhenOnlyTheLegacyDeveloperModeKeyIsOff()
    {
        Assert.Equal(StatusCodes.Status404NotFound, await ListStatus(("IntoChat:DeveloperMode", "false")));
    }

    [Fact]
    public async Task CSharpAuthoringIsNotFoundWhenDeveloperModeIsUnparseable()
    {
        Assert.Equal(StatusCodes.Status404NotFound, await ListStatus(("IntoChat:DeveloperMode", "maybe")));
    }

    private static async Task<int> ListStatus(params (string Key, string Value)[] settings)
    {
        var (app, endpoints) = MapAuthoringRoutes(settings);
        var list = endpoints.Single(route => route.RoutePattern.RawText == "/brains/{brainId}/csharp/"
            && route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.RouteValues["brainId"] = "brain-1";

        await list.RequestDelegate!(context);

        return context.Response.StatusCode;
    }

    private static (WebApplication App, RouteEndpoint[] Endpoints) MapAuthoringRoutes(params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
        builder.Services.AddSingleton(new ScopedCSharpTools(null!, null!, null, "scope", canRun: false));
        builder.Services.AddSingleton(new CSharpSharing(null!, null!));
        var app = builder.Build();
        CSharpAuthoringEndpoints.Map(app);
        return (app, ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray());
    }
}
