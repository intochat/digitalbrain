using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace DigitalBrain.Core;

public static class BrainHosting
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo)
    {
        AddOptions(silo.Services);
        return silo;
    }
    public static IClientBuilder AddDigitalBrain(this IClientBuilder client)
    {
        AddOptions(client.Services);
        client.Services.TryAddSingleton<BrainClient>();
        client.Services.TryAddSingleton<IDigitalBrain>(services => services.GetRequiredService<BrainClient>());
        return client;
    }
    private static void AddOptions(IServiceCollection services) => services.AddOptions<BrainOptions>()
        .Validate(o => o.BufferCapacity > 0 && o.RenewEvery > TimeSpan.Zero && o.OperationTimeout > TimeSpan.Zero
            && o.ObserverLease > o.RenewEvery + o.OperationTimeout, "Invalid brain buffer or subscription timing settings.")
        .ValidateOnStart();
}
