using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Compute;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Modules.Compute.Tests.Unit;

public sealed class ComputeRouteFacts
{
    [Fact]
    public void ComputeSummaryAndLimitsAreAccountRoutesOutsideTheBrainGroup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new ComputeModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Equal(["/compute/limits", "/compute/summary"], routes.Order().ToArray());
    }
}
