using DigitalBrain.Contracts;

namespace DigitalBrain.Receipts.Signals;

[GenerateSerializer, Alias("intent.recorded")]
public sealed record IntentRecorded(
    [property: Id(0)] string IntentId,
    [property: Id(1)] ReceiptOutcome Outcome,
    [property: Id(2)] int ModelCalls,
    [property: Id(3)] decimal Compute,
    [property: Id(4)] int TouchedSources,
    [property: Id(5)] DateTimeOffset OccurredAt) : Signal;
