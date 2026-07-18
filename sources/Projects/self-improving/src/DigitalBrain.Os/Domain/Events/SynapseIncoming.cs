using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public sealed record SynapseIncoming(NeuronId Receiver, Synapse Payload) : Synapse;
