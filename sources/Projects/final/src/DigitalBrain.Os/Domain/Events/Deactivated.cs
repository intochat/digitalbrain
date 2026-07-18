using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public sealed record Deactivated(NeuronId Neuron) : Synapse;
