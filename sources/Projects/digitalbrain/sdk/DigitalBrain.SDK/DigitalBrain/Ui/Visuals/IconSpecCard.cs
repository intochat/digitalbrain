namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

public sealed record IconSpecCardPayload(
    string NeuronFqn,
    uint Seed,
    string Tone,
    string ShapeHint);
