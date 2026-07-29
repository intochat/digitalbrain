using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Behaviors;

public static class BehaviorHostHosting
{
    public static IServiceCollection AddBehaviorHostEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.TryAddSingleton<IBehaviorArtifactTrust>(static provider =>
            new BehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));
        services.TryAddSingleton<BehaviorHostEngine>();
        services.TryAddSingleton<IBehaviorHostGateway>(static provider =>
            provider.GetRequiredService<BehaviorHostEngine>());
        return services;
    }
}
