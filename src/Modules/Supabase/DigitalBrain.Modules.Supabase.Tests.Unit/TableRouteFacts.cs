using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Supabase.Tests;

public sealed class TableRouteFacts
{
    [Fact]
    public void TablesAreServedOnlyUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new SupabaseModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Equal(["/brains/{brainId}/tables/{tableId}", "/brains/{brainId}/tables/{tableId}/view"], routes.Order().ToArray());
    }
}
