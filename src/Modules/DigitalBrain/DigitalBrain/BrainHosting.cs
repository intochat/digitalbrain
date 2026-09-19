using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace DigitalBrain.Core;
public static class BrainHosting
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo)
    {
        silo.Services.AddOptions<BrainOptions>();
        return silo;
    }

    public static IClientBuilder AddDigitalBrain(this IClientBuilder client)
    {
        client.Services.AddOptions<BrainOptions>();
        client.Services.TryAddSingleton<BrainClient>();
        client.Services.TryAddSingleton<IDigitalBrain>(services => services.GetRequiredService<BrainClient>());
        return client;
    }
}
