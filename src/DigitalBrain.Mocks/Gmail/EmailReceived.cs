namespace DigitalBrain.Mocks;

public sealed record EmailReceived(
    string MessageId,
    string From,
    string Domain,
    string Subject,
    string Snippet) : Synapse;
