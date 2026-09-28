using DigitalBrain.Apps.Manifests;
using DigitalBrain.Core;
using DigitalBrain.Discovery;
using DigitalBrain.Specs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Apps;

[ModuleConfiguration(typeof(AppsConfigurationContract))]
public sealed class AppsModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(AppsModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilitySource, AppCapabilitySource>());
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<StepLibrary, AppSteps>());
    }
}
