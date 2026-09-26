using DigitalBrain.Core.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace DigitalBrain.Core;

public static class BrainHosting
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo)
    {
        silo.Services.AddDigitalBrainClient();
        silo.Services.TryAddSingleton<LocalSignalHub>();
        silo.Services.TryAddSingleton<ILocalSignalHub>(sp => sp.GetRequiredService<LocalSignalHub>());
        return silo;
    }
    public static ISiloBuilder AddNeuronRegistry(this ISiloBuilder silo, IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        silo.Services.AddSingleton(new NeuronRegistry(moduleTypes));
        silo.AddStartupTask<NeuronDiscoveryStartupTask>();
        return silo;
    }
}
