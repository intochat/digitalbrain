using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Compute.Usage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Assistant.Tests;

public sealed class AssistantRouteFacts
{
    [Fact]
    public void ComputeUsageAndAssistantApplicationsAreServedUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton(typeof(IUsageStore), _ => null!);
        builder.Services.AddSingleton(typeof(PackageService), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new AssistantModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/compute/usage", routes);
        Assert.Contains("/brains/{brainId}/applications/assistant/open", routes);
        Assert.Contains("/brains/{brainId}/applications/assistant/start", routes);
        Assert.Contains("/brains/{brainId}/built-in/activate", routes);
        Assert.Contains("/brains/{brainId}/built-in/assistant/open", routes);
        Assert.All(routes.Where(route => !route.StartsWith("/ai/", StringComparison.Ordinal) && route != "/agent" && !route.StartsWith("/packages", StringComparison.Ordinal)), route => Assert.StartsWith("/brains/{brainId}/", route));
    }
}
