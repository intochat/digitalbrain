namespace DigitalBrain.Poc.Runtime;

internal sealed record JournalEntry(
    string ReceiptId,
    string Kind,
    string Direction,
    string? PayloadJson = null);
