namespace DigitalBrain.Runtime.Ui;

public sealed record InoSourceCardPayload(
    string CorrelationId,
    string NeuronFqn,
    IReadOnlyList<string> Chunks,
    bool IsFinal);
