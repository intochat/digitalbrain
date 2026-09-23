using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Marketplace;

[ModuleConfiguration(typeof(MarketplaceConfigurationContract))]
public sealed class MarketplaceModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(MarketplaceModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.AddMarketplaceEarnings();
        services.TryAddSingleton<INamespaceProofAuthority, DnsTxtNamespaceProofAuthority>();
        services.TryAddSingleton<IAppSignatureVerifier, Ed25519AppSignatureVerifier>();
        services.TryAddSingleton<IManifestScanner, DefaultManifestScanner>();
        services.TryAddSingleton<IGoldenPromptEvaluator, GoldenPromptEvaluator>();
        services.TryAddSingleton<IAppUsageLedger, InMemoryAppUsageLedger>();
        services.TryAddSingleton<IManifestScenarioRunner, BrokerScenarioRunner>();
        services.TryAddSingleton<ICertificationService, CertificationService>();
    }
}
