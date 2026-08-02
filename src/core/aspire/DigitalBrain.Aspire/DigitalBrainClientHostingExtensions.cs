using DigitalBrain.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainClientHostingExtensions
{
    public const string DefaultOwner = "dev";
    public const string OwnerConfigurationKey = "DigitalBrain:Owner";

    public static string ResolveOwner(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var owner = configuration[OwnerConfigurationKey];
        return string.IsNullOrWhiteSpace(owner) ? DefaultOwner : owner;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(
        this IHostApplicationBuilder builder,
        Action<IClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return AddDigitalBrainClient(builder, ResolveOwner(builder.Configuration), configure);
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(
        this IHostApplicationBuilder builder,
        string owner,
        Action<IClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        builder.UseOrleansClient(client =>
        {
            client.Services.AddDigitalBrainClientWireSerializers();
            configure?.Invoke(client);
        });

        builder.Services.AddDigitalBrainOwner(
            builder.Configuration,
            owner,
            activateOnStart: true);
        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainOwner(
        this IHostApplicationBuilder builder,
        string? owner = null,
        bool activateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddDigitalBrainOwner(builder.Configuration, owner, activateOnStart);
        return builder;
    }

    public static IServiceCollection AddDigitalBrainOwner(
        this IServiceCollection services,
        IConfiguration configuration,
        string? owner = null,
        bool activateOnStart = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var resolvedOwner = string.IsNullOrWhiteSpace(owner)
            ? ResolveOwner(configuration)
            : owner!;
        services.AddSingleton<IDigitalBrain>(
            serviceProvider => DigitalBrainClient.Connect(
                serviceProvider.GetRequiredService<IGrainFactory>(),
                resolvedOwner));
        if (activateOnStart)
        {
            services.AddHostedService<DigitalBrainActivationHostedService>();
        }

        return services;
    }
}

