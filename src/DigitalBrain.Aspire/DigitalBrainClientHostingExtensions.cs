using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainClientHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder, string owner)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        builder.UseOrleansClient();
        builder.Services.AddSingleton(
            services => DigitalBrainClient.Connect(
                services.GetRequiredService<IGrainFactory>(),
                owner));

        return builder;
    }
}
