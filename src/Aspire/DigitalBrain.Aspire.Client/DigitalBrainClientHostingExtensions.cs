using DigitalBrain.Client;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire.Client;

public static class DigitalBrainClientHostingExtensions
{
    public static TBuilder AddDigitalBrainClient<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddDigitalBrainClientDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.UseOrleansClient(client => client.AddDigitalBrain());
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live"),
        }).AllowAnonymous();
        return app;
    }

}
