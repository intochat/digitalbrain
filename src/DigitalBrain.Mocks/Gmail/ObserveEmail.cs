namespace DigitalBrain.Mocks;

public sealed record ObserveEmail(
    string MessageId,
    string From,
    string Domain,
    string Subject,
    string Snippet) : Synapse;
