using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Runtime.Tests;

internal sealed record IncrementAndEmit(string ReceiptId = "incrementing-input-1") :
    Synapse,
    IReceiptIdentity;
