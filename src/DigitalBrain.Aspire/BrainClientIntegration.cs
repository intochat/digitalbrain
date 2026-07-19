using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace DigitalBrain;

public static class BrainClientIntegration
{
    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder, string owner)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        builder.UseOrleansClient();
        builder.Services.AddSingleton(services => new BrainClient(services.GetRequiredService<IGrainFactory>(), new OwnerId(owner)));

        return builder;
    }
}
