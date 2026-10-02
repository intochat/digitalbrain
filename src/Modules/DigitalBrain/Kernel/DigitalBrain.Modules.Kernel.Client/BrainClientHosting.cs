using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Client;

public static class BrainClientHosting
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

    public static IClientBuilder AddDigitalBrain(this IClientBuilder client)
    {
        client.Services.AddDigitalBrainClient();
        return client;
    }

    public static IServiceCollection AddDigitalBrainClient(this IServiceCollection services)
    {
        services.AddOptions<BrainOptions>()
            .Validate(o => o.BufferCapacity > 0 && o.RenewEvery > TimeSpan.Zero && o.OperationTimeout > TimeSpan.Zero
                && o.ObserverLease > o.RenewEvery + o.OperationTimeout, "Invalid brain buffer or subscription timing settings.")
            .ValidateOnStart();
        services.TryAddSingleton<IDigitalBrain, BrainClient>();
        return services;
    }
}
