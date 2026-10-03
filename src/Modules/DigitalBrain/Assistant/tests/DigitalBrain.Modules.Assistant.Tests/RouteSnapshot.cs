using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests;

internal sealed record RouteSnapshot(IReadOnlyList<RouteEndpoint> Endpoints)
{
    public string[] Routes => [.. Endpoints.Select(endpoint => endpoint.RoutePattern.RawText!)];

    public string[] MethodAndRouteKeys => [.. Endpoints.Select(endpoint =>
        string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []) + " " + endpoint.RoutePattern.RawText)];

    public static RouteSnapshot Map(Action<IServiceCollection> registerServices, Action<IEndpointRouteBuilder> mapRoutes)
    {
        var builder = WebApplication.CreateBuilder();
        registerServices(builder.Services);
        IEndpointRouteBuilder app = builder.Build();
        mapRoutes(app);
        return new([.. app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()]);
    }

    public void AssertEveryMethodAndRouteIsDistinct()
        => Assert.Equal(MethodAndRouteKeys.Length, MethodAndRouteKeys.Distinct().Count());
}
