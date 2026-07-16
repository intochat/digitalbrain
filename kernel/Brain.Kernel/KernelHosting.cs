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
        foreach (var kind in kinds.Append(new EffectKind()))
            silo.Services.AddKeyedSingleton<INeuronKind>(kind.Kind, kind);
        return silo;
    }
}
