using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Behaviors;

public static class BehaviorHostHosting
{
    public const string BrokerBaseAddressConfigurationKey = "DigitalBrain:Behaviors:Broker:BaseAddress";
    public const string BrokerHttpClientName = "DigitalBrain.Behaviors.Broker";

    // Explicit TestingAppHost-only switch. Must never be set in product AppHost.
    // Enables an unprotected in-process payload seed/broker for L2 until silo reverse-broker exists.
    public const string TestingInProcessPayloadBrokerConfigurationKey =
        "DigitalBrain:Behaviors:Broker:TestingInProcessPayloadBroker";

    public static IServiceCollection AddBehaviorHostEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.TryAddSingleton<IBehaviorArtifactTrust>(static provider =>
            new BehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));

        var brokerBaseAddress = configuration[BrokerBaseAddressConfigurationKey];
        if (!string.IsNullOrWhiteSpace(brokerBaseAddress))
        {
            if (!Uri.TryCreate(brokerBaseAddress, UriKind.Absolute, out var absoluteAddress))
            {
                throw new InvalidOperationException(
                    $"Configuration '{BrokerBaseAddressConfigurationKey}' must be an absolute URI.");
            }

            services.AddHttpClient(BrokerHttpClientName, client =>
            {
                client.BaseAddress = absoluteAddress;
            });
            services.TryAddSingleton<IBehaviorHostBrokerClientFactory, HttpBehaviorHostBrokerClientFactory>();
        }
        else if (IsTestingInProcessPayloadBrokerEnabled(configuration))
        {
            services.TryAddSingleton<InMemoryBehaviorHostPayloadStore>();
            services.TryAddSingleton<IBehaviorHostBrokerClientFactory, InMemoryBehaviorHostBrokerClientFactory>();
        }

        services.TryAddSingleton(static provider =>
            new BehaviorHostEngine(
                provider.GetRequiredService<IBehaviorArtifactTrust>(),
                provider.GetService<IBehaviorHostBrokerClientFactory>()));
        services.TryAddSingleton<IBehaviorHostGateway>(static provider =>
            provider.GetRequiredService<BehaviorHostEngine>());
        return services;
    }

    public static bool IsTestingInProcessPayloadBrokerEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[TestingInProcessPayloadBrokerConfigurationKey];
        return bool.TryParse(value, out var enabled) && enabled;
    }
}
