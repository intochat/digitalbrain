using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Kernel;

public static class BrainHosting
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo)
        => silo.AddDigitalBrain([]);

    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo, IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.AddSingleton(new ModuleInventory(moduleTypes));
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.AddStartupTask<RuntimeStartupTask>();
        silo.Services.TryAddSingleton<ICallFilter, CallFilter>();
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IIncomingGrainCallFilter, NeuronCallFilter>());
        silo.Services.TryAddSingleton<LocalSignalHub>();
        silo.Services.TryAddSingleton<DigitalBrain.Contracts.Signals.ILocalSignalHub>(sp => sp.GetRequiredService<LocalSignalHub>());
        silo.Services.AddOptions<ObserverOptions>().Validate(options => options.Lease > TimeSpan.Zero, "Observer lease must be positive.").ValidateOnStart();
        return silo;
    }
}
