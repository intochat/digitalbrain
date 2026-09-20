namespace DigitalBrain.Mcp;

public sealed record GetResult(string Session, string Neuron, string GrainId);

public sealed record PingResult(string Neuron, string Activation);

public sealed record ObserveResult(string Neuron, int Seconds, IReadOnlyList<SignalEntry> Signals);

public sealed record SignalEntry(string Type, string Payload);