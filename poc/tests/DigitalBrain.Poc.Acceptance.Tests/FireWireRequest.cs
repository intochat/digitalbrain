namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record FireWireRequest(string SessionToken, string ReceiptId, string? Value);
