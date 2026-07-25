using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainClientHostingExtensions
{
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
            services => DigitalBrainClient.Connect(
                services.GetRequiredService<IGrainFactory>(),
                owner));

        return builder;
    }
}
