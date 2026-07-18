using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Navigator;

public sealed class NavigatorRouter(IGrainFactory grains)
{
    private async Task<IReadOnlyList<NeuronCatalogEntry>> GetCombinedEntriesAsync()
    {
        var activeScope = BrainScopeHelper.GetActiveScope();
        var globalCatalog = grains.GetGrain<IBrainCatalog>(BrainScopeHelper.GlobalScope);
        var entries = await globalCatalog.ListRegisteredAsync();

        if (!activeScope.Equals(BrainScopeHelper.GlobalScope, StringComparison.OrdinalIgnoreCase))
        {
            var privateCatalog = grains.GetGrain<IBrainCatalog>(activeScope);
            var privateEntries = await privateCatalog.ListRegisteredAsync();
            return privateEntries.Concat(entries).ToList();
        }

        return entries;
    }

    public async Task<NeuronCatalogEntry> ResolveHandlerAsync(string synapseTypeFullName, string? receiverNeuronType = null)
    {
        var entries = await GetCombinedEntriesAsync();
        if (receiverNeuronType != null)
        {
            var matched = entries.FirstOrDefault(e => 
                e.HandledSynapseTypes.Contains(synapseTypeFullName) && 
                (e.TypeFullName.Equals(receiverNeuronType, StringComparison.OrdinalIgnoreCase) || 
                 ImplicitSubscriptionNamespace(e).Equals(receiverNeuronType, StringComparison.OrdinalIgnoreCase)));
            if (matched != null) return matched;
        }

        return entries.FirstOrDefault(e => e.HandledSynapseTypes.Contains(synapseTypeFullName))
            ?? throw new InvalidOperationException(
                $"No registered neuron handles synapse type '{synapseTypeFullName}'. " +
                $"Known: {string.Join("; ", entries.SelectMany(e => e.HandledSynapseTypes))}.");
    }

    // E-RUN #37. Signals are broadcast — every neuron that declares
    // `on signal(T):` runs, not just the first match. ResolveHandlerAsync
    // keeps its single-match semantics for the point-to-point synapse path
    // (#36); subscribers go through this multi-match path instead. An empty
    // result is normal: a signal with no subscribers is valid (the signal
    // log still gets the append, observers can backfill).
    public async Task<IReadOnlyList<NeuronCatalogEntry>> ResolveSubscribersAsync(string signalTypeFullName)
    {
        var entries = await GetCombinedEntriesAsync();
        return entries
            .Where(entry => entry.HandledSignalSubscriptions.Contains(signalTypeFullName))
            .ToArray();
    }

    public static string ImplicitSubscriptionNamespace(NeuronCatalogEntry entry)
    {
        // Convention: [ImplicitStreamSubscription(nameof(X))] — namespace is the short class name.
        var fullName = entry.TypeFullName;
        var idx = fullName.LastIndexOf('.');
        return idx < 0 ? fullName : fullName[(idx + 1)..];
    }
}
