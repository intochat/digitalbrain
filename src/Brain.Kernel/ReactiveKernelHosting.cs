using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace Brain.Kernel;

public static class ReactiveKernelHosting
{
    public static ISiloBuilder AddReactiveNeuronJournaling(this ISiloBuilder silo)
    {
        silo.UseJsonJournalFormat(ReactiveJournalJsonContext.Default);
        silo.AddJournalStorage();
        return silo;
    }
}
