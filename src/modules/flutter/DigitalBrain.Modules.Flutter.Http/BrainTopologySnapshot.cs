namespace DigitalBrain.UI;

internal sealed record BrainTopologySnapshot(
    IReadOnlyList<BrainModule> Modules,
    IReadOnlyList<BrainNeuron> Neurons,
    DateTimeOffset ObservedAt);

internal sealed record BrainModule(string Id);

internal sealed record BrainNeuron(
    string Id,
    string GrainType,
    string Identity,
    string Silo);
