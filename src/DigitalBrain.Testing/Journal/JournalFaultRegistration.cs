namespace DigitalBrain.Testing;

internal sealed record JournalFaultRegistration(NeuronId Target, string Message, Task Consumed, object Token);
