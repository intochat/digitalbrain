using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Core;

public static class BrainClientHosting
{
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
        services.TryAddSingleton<BrainClient>();
        services.TryAddSingleton<IDigitalBrain>(sp => sp.GetRequiredService<BrainClient>());
        services.TryAddSingleton<ICallFilter, CallFilter>();
        return services;
    }
}
