using DigitalBrain.Microsoft.CSharp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class CSharpRouteFacts
{
    [Fact]
    public void CSharpAuthoringIsServedOnlyUnderTheBrainRoute()
    {
        var (_, endpoints) = MapAuthoringRoutes();
        var routes = endpoints.Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "DELETE /brains/{brainId}/csharp/{id}",
                "GET /brains/{brainId}/csharp/",
                "GET /brains/{brainId}/csharp/{id}",
                "POST /brains/{brainId}/csharp/{id}/share",
                "POST /brains/{brainId}/csharp/{id}/start",
                "POST /brains/{brainId}/csharp/{id}/stop",
                "PUT /brains/{brainId}/csharp/{id}",
            ],
            routes);
    }

    [Fact]
    public async Task CSharpAuthoringIsNotFoundWhenDeveloperModeIsOff()
    {
        Assert.Equal(StatusCodes.Status404NotFound, await ListStatus(("DigitalBrain:DeveloperMode", "false")));
    }

    [Fact]
    public async Task CSharpAuthoringIsNotFoundWhenDeveloperModeIsUnparseable()
    {
        Assert.Equal(StatusCodes.Status404NotFound, await ListStatus(("DigitalBrain:DeveloperMode", "maybe")));
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
