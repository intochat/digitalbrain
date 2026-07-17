using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling.Json;

namespace Brain.Kernel;

public static class KernelHosting
{
    public static ISiloBuilder AddBrainKernel(this ISiloBuilder silo, params INeuronKind[] kinds)
    {
        silo.UseJsonJournalFormat(NeuronJournalJsonContext.Default);
        silo.Services.AddSingleton<IAttributeToFactoryMapper<NeuronStateAttribute>, NeuronStateMapper>();
        var catalog = GetOrAddCatalog(silo);
        foreach (var kind in kinds.Append(new EffectKind()))
        {
            silo.Services.AddKeyedSingleton<INeuronKind>(kind.Kind, kind);
            catalog.Add(kind.Kind, kind.Contracts);
        }
        AddCatalogKindOnce(silo, catalog);
        return silo;
    }

    public static ISiloBuilder AddBrainKind(this ISiloBuilder silo, string kind, Func<IServiceProvider, INeuronKind> factory)
    {
        silo.Services.AddKeyedSingleton<INeuronKind>(kind, (sp, _) => factory(sp));
        GetOrAddCatalog(silo).Add(kind, ["*"]);
        return silo;
    }

    private static KindCatalog GetOrAddCatalog(ISiloBuilder silo)
    {
        var descriptor = silo.Services.FirstOrDefault(service =>
            service.ServiceType == typeof(KindCatalog) && service.ImplementationInstance is KindCatalog);
        if (descriptor?.ImplementationInstance is KindCatalog existing)
            return existing;

        var catalog = new KindCatalog();
        silo.Services.AddSingleton(catalog);
        return catalog;
    }

    private static void AddCatalogKindOnce(ISiloBuilder silo, KindCatalog catalog)
    {
        var alreadyRegistered = silo.Services.Any(service =>
            service.ServiceType == typeof(INeuronKind) && service.IsKeyedService && Equals(service.ServiceKey, "catalog"));
        if (alreadyRegistered)
            return;

        var catalogKind = new CatalogKind(catalog);
        silo.Services.AddKeyedSingleton<INeuronKind>(catalogKind.Kind, catalogKind);
        catalog.Add(catalogKind.Kind, catalogKind.Contracts);
    }
}
