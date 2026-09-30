using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Apps.Tests;

public sealed class AppsRouteFacts
{
    [Fact]
    public void AppCatalogAndConsentAreServedOnlyUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new AppsModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/apps/", routes);
        Assert.Contains("/brains/{brainId}/apps/{appId}/consent/approve", routes);
        Assert.All(routes, route => Assert.StartsWith("/brains/{brainId}/apps", route));
    }
}
