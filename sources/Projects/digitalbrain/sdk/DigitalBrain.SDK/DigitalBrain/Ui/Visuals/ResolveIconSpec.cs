using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record ResolveIconSpec([property: Id(1)] string NeuronFqn
) : Synapse;
