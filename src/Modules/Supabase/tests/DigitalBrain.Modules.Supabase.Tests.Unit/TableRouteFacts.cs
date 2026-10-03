using DigitalBrain.Supabase;
using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit;

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
            .OfType<RouteEndpoint>().Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["GET /brains/{brainId}/tables/{tableId}", "POST /brains/{brainId}/tables/{tableId}/view"], routes);
    }
}
