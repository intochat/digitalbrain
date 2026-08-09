using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Runtime.Tests;

internal sealed record ThrowAfterStateAndEmit(string ReceiptId = "throwing-input-1") :
    Synapse,
    IReceiptIdentity;
