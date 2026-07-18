using Orleans;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Neuron;

/// <summary>
/// A specialized Cognitive Diff Analysis neuron.
/// </summary>
[GrainType(NeuronTargetFqn)]
internal sealed class CognitiveDiff : Llm
{
    public new const string NeuronTargetFqn = "DigitalBrain.Custom.CognitiveDiff";

    public CognitiveDiff() : base()
    {
    }
}
