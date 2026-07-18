using DigitalBrain.V2.Core.Synapses;
using Orleans.Streams;

namespace DigitalBrain.V2.Core.Runtime;

// The single shared timeline every broadcast flows through. One namespace, one well-known
// id — the minimum that still routes correctly. A sharded subscription registry can replace
// this later without changing the Neuron API.
public static class SynapseStream
{
    public const string ProviderName = "Timeline";
    public const string Namespace = "timeline";
    public static readonly Guid WellKnownId = Guid.Empty;

    public static IAsyncStream<Synapse> Timeline(this IStreamProvider provider) =>
        provider.GetStream<Synapse>(StreamId.Create(Namespace, WellKnownId));
}
