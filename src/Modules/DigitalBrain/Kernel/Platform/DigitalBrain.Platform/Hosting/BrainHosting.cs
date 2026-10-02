using DigitalBrain.Client;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Platform.Hosting;

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
        silo.Services.AddDigitalBrainClient();
        silo.Services.TryAddSingleton<ICallFilter, CallFilter>();
        silo.Services.TryAddSingleton<LocalSignalHub>();
        silo.Services.TryAddSingleton<ILocalSignalHub>(sp => sp.GetRequiredService<LocalSignalHub>());
        silo.AddPlatform();
        return silo;
    }
}
