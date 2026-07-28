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

        if (configure is null)
        {
            builder.UseOrleansClient();
        }
        else
        {
            builder.UseOrleansClient(configure);
        }

        builder.Services.AddSingleton<IDigitalBrain>(
            services => DigitalBrainClient.Connect(services.GetRequiredService<IGrainFactory>(), owner));
        builder.Services.AddHostedService<DigitalBrainActivationHostedService>();

        return builder;
    }
}

