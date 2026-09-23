using DigitalBrain.Broker.Hosting;
using DigitalBrain.Broker.Packages;
using DigitalBrain.Broker.Sandbox;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Broker;

[ModuleConfiguration(typeof(BrokerConfigurationContract))]
public sealed class BrokerModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(BrokerModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton<IAppObservationStore>(_ => new InMemoryAppObservationStore());
        services.TryAddSingleton<IEgressPolicy>(_ => new DataClassEgressPolicy([]));
        services.TryAddSingleton<IRemoteAppTransport>(_ => new HttpRemoteAppTransport());
        services.TryAddSingleton<IRemoteAppGateway, BrokerGateway>();

        services.AddOptions<SandboxOptions>().BindConfiguration(SandboxOptions.SectionName);
        services.TryAddSingleton<BannedApiAnalyzer>();
        services.TryAddSingleton<IProcessAppPackageReader, NupkgProcessAppPackageReader>();
        services.TryAddSingleton<IProcessPackageVerifier>(sp => new EcdsaProcessPackageVerifier(
            sp.GetRequiredService<IOptions<SandboxOptions>>().Value.TrustedPublisherKeys,
            sp.GetRequiredService<BannedApiAnalyzer>()));
        services.TryAddSingleton<ISandboxRuntime, DockerSandboxRuntime>();
        services.TryAddSingleton<IProcessAppGateway, ProcessAppGateway>();
    }
}