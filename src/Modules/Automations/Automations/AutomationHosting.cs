using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Automations;

public static class AutomationHosting
{
    public static ISiloBuilder AddAutomations(this ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton<IAutomationActionCatalog, EmptyAutomationActionCatalog>();
        silo.Services.TryAddSingleton<IAutomationActionInvoker, UnavailableAutomationActionInvoker>();
        silo.Services.TryAddSingleton<IAutomationJournal, GrainAutomationJournal>();
        return silo;
    }
}
