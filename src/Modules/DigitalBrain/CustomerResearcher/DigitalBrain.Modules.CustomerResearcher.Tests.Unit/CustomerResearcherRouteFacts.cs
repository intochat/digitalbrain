using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.CustomerResearcher.Tests;

public sealed class CustomerResearcherRouteFacts
{
    [Fact]
    public void CustomerResearcherOpensOnlyUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new CustomerResearcherModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Equal(["/brains/{brainId}/applications/customer-researcher/open"], routes);
    }
}
