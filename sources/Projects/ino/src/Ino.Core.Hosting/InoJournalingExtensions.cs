using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;

namespace Ino.Core.Hosting;

public static class InoJournalingExtensions
{
    // Wires the persistence stack Neuron<TEvent> needs (IStateMachineStorageProvider +
    // AddStateMachineStorage). v0.1 uses the in-memory volatile provider on every
    // silo — it matches the "decay implementation pending" status in the product
    // vision and keeps the cold-boot demo storage-free. A Redis-backed provider
    // is a swap-in for post-v0.1.
    public static ISiloBuilder UseInoJournaling(this ISiloBuilder silo)
    {
        silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        silo.AddStateMachineStorage();
        return silo;
    }
}
