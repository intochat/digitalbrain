using DigitalBrain.Apps.Manifests;
using DigitalBrain.Core;
using DigitalBrain.Discovery;
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
        // Discovery indexes the committed first-party manifests; apps are composed after discovery
        // in the product profile, so this replaces the empty default source.
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IManifestSource, FirstPartyManifestSource>());
        silo.Services.TryAddSingleton<AppBehaviorRegistry>();
        silo.Services.TryAddSingleton<WorkspaceApps>();
        foreach (var id in new[] { "text.prefix", "text.suffix", "text.uppercase", "text.lowercase", "text.trim", "text.identity" })
        { silo.Services.AddSingleton<IAppBehavior>(new TextAppBehavior(id)); }
    }
}
