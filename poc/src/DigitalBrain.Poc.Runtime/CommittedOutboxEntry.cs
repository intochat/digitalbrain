namespace DigitalBrain.Poc.Runtime;

public sealed record CommittedOutboxEntry(
    string DeliveryId,
    string ReceiptId,
    int OutputOrdinal,
    string Kind);
