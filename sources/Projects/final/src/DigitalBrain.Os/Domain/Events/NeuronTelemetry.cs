using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public sealed record NeuronTelemetry(
    NeuronId Neuron,
    string Event,
    Dictionary<string, string> Data) : Synapse;  // concrete Dictionary for reliable Orleans copier generation