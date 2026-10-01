using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AppsRouteFacts
{
    [Fact]
    public void AppsCompositionMapsDraftsVerificationAndBrainScopedDiscussions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton<MarketplaceService>(_ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new AppsModule().Configure(app);
        var routes = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        Assert.Contains("/packages/drafts/{id}/build", routes);
        Assert.Contains("/packages/{owner}/{name}/verify", routes);
        Assert.Contains("/brains/{brainId}/packages/{owner}/{name}/invocations/{invocationId:guid}/discussion", routes);
    }

    [Fact]
    public void AppCatalogAndConsentAreServedOnlyUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton<MarketplaceService>(_ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new AppsModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/apps/", StringComparison.Ordinal)).Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "GET /brains/{brainId}/apps/",
                "GET /brains/{brainId}/apps/{appId}/consent",
                "POST /brains/{brainId}/apps/{appId}/consent/approve",
            ],
            routes);
    }
}

