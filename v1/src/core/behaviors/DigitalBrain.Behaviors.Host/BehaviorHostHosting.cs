using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Behaviors.Host;

public static class BehaviorHostHosting
{
    public const string BrokerBaseAddressConfigurationKey = "DigitalBrain:Behaviors:Broker:BaseAddress";
    public const string BrokerCredentialConfigurationKey = BehaviorBrokerContract.CredentialConfigurationKey;
    public const string BrokerCredentialHeaderName = BehaviorBrokerContract.CredentialHeaderName;
    public const string BrokerHttpClientName = "DigitalBrain.Behaviors.Broker";

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

            var brokerCredential = configuration[BrokerCredentialConfigurationKey];
            if (string.IsNullOrWhiteSpace(brokerCredential))
            {
                throw new InvalidOperationException(
                    $"Configuration '{BrokerCredentialConfigurationKey}' is required when the reverse broker base address is set.");
            }

            services.AddHttpClient(BrokerHttpClientName, client =>
            {
                client.BaseAddress = absoluteAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    BrokerCredentialHeaderName,
                    brokerCredential);
            });
            services.TryAddSingleton<IBehaviorHostBrokerClientFactory, HttpBehaviorHostBrokerClientFactory>();
        }

        services.TryAddSingleton(static provider =>
            new BehaviorHostEngine(
                provider.GetRequiredService<IBehaviorArtifactTrust>(),
                provider.GetService<IBehaviorHostBrokerClientFactory>()));
        services.TryAddSingleton<IBehaviorHostGateway>(static provider =>
            provider.GetRequiredService<BehaviorHostEngine>());
        return services;
    }
}
