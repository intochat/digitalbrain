using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Client.Orleans;

public static class BrainClientHosting
{
    public static IClientBuilder AddDigitalBrain(this IClientBuilder client)
    {
        client.Services.AddDigitalBrainClient();
        return client;
    }

    public static IServiceCollection AddDigitalBrainClient(this IServiceCollection services)
    {
        services.AddOptions<SubscriptionOptions>()
            .Validate(o => o.BufferCapacity > 0 && o.RenewEvery > TimeSpan.Zero && o.OperationTimeout > TimeSpan.Zero, "Invalid brain buffer or subscription timing settings.")
            .ValidateOnStart();
        services.TryAddSingleton<IDigitalBrain, BrainClient>();
        return services;
    }
}
