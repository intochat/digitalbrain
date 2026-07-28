using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace DigitalBrain.ServiceDefaultsTests;

public sealed class ServiceDefaultsContracts
{
    [Fact(DisplayName = "service defaults expose healthy readiness and liveness endpoints")]
    public async Task ServiceDefaultsExposeHealthyReadinessAndLivenessEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "";
        builder.AddServiceDefaults();

        await using var app = builder.Build();
        app.MapDefaultEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var health = await app.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(["/alive", "/health"], routes);
    }
}
