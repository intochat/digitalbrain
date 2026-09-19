using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace DigitalBrain.Core;

public static class BrainHosting
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo)
    {
        AddBrainClient(silo.Services);
        silo.Services.TryAddSingleton<LocalSignalHub>();
        return silo;
    }
    public static IClientBuilder AddDigitalBrain(this IClientBuilder client)
    {
        AddBrainClient(client.Services);
        return client;
    }
    private static void AddBrainClient(IServiceCollection services)
    {
        AddOptions(services);
        services.TryAddSingleton<BrainClient>();
        services.TryAddSingleton<IDigitalBrain>(sp => sp.GetRequiredService<BrainClient>());
    }
    private static void AddOptions(IServiceCollection services) => services.AddOptions<BrainOptions>()
        .Validate(o => o.BufferCapacity > 0 && o.RenewEvery > TimeSpan.Zero && o.OperationTimeout > TimeSpan.Zero
            && o.ObserverLease > o.RenewEvery + o.OperationTimeout, "Invalid brain buffer or subscription timing settings.")
        .ValidateOnStart();
}
