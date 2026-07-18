using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling.Json;

namespace Brain.Kernel;

public static class KernelHosting
{
    public static ISiloBuilder AddBrainKernel(this ISiloBuilder silo)
    {
        silo.UseJsonJournalFormat(NeuronJournalJsonContext.Default);
        silo.Services.AddSingleton<IAttributeToFactoryMapper<NeuronStateAttribute>, NeuronStateMapper>();
        return silo;
    }
}
