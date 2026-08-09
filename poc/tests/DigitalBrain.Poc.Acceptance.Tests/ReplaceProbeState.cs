using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record ReplaceProbeState(
    string Value,
    string ReceiptId = "oversized-input-1") : Synapse, IReceiptIdentity;
