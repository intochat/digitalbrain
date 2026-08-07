namespace DigitalBrain.Testing;

internal sealed record JournalFaultRegistration(
    ScopeKey Scope,
    NeuronId Target,
    string Message,
    Task Consumed,
    object Token);
