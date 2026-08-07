using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Introspection.IntrospectionNeuron", "DigitalBrain.Introspection.JournalPageRead", false),
        ("DigitalBrain.Introspection.IntrospectionNeuron", "DigitalBrain.Introspection.JournalTallied", false),
        ("DigitalBrain.Introspection.IntrospectionNeuron", "DigitalBrain.Introspection.ReadJournalRequest", true),
        ("DigitalBrain.Introspection.IntrospectionNeuron", "DigitalBrain.Introspection.ReadTopologyRequest", true),
        ("DigitalBrain.Introspection.IntrospectionNeuron", "DigitalBrain.Introspection.TallyJournalRequest", true),
        ("DigitalBrain.Introspection.IntrospectionNeuron", "DigitalBrain.Introspection.TopologyRead", false),
    ];
}
