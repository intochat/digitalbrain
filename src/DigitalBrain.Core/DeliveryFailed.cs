namespace DigitalBrain;

public sealed record DeliveryFailed(
    SynapseReference Synapse,
    NeuronId Receiver,
    string Reason,
    int Attempts) : Synapse;
