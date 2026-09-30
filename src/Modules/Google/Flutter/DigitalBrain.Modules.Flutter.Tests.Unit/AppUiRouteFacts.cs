using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Flutter.Tests;

public sealed class AppUiRouteFacts
{
    [Fact]
    public void AppUiNodesAndEventsAreServedUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        AppUiEndpoints.Map(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Equal(["/brains/{brainId}/apps/event", "/brains/{brainId}/apps/node"], routes.Order().ToArray());
    }
}
