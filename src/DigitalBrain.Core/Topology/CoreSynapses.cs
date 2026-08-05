namespace DigitalBrain;

public sealed record Connect(string Fact, NeuronId To) : Synapse;

public sealed record Disconnect(string Fact, NeuronId To) : Synapse;

public sealed record ConnectionRefused(SynapseRef Request, string Fact, NeuronId To, string Reason) : Synapse;

public sealed record DeliveryFailed(SynapseRef Fact, NeuronId Receiver, string Reason, int Attempts) : Synapse;

public sealed record AskExpired(SynapseRef Ask, string Question) : Synapse;

public sealed record Schedule(Synapse Fact, TimeSpan Period) : Synapse;

public sealed record Unschedule(string Fact) : Synapse;

public sealed record ScheduleFailed(string Fact, string Reason, int ConsecutiveFailures) : Synapse;
