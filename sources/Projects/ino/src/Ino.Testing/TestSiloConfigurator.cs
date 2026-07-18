using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace Ino.Testing;

/// <summary>
/// Configures the Orleans silo inside a TestCluster with the persistence Neuron&lt;TEvent&gt;
/// needs: an in-memory IStateMachineStorageProvider that backs every IDurableList /
/// IDurableDictionary used by DurableGrain-based neurons.
///
/// Production silos swap VolatileStateMachineStorageProvider for a Redis-backed
/// implementation; the neuron code is oblivious to the difference.
/// </summary>
public sealed class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        silo.AddStateMachineStorage();
    }
}
