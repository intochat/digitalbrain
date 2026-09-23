using DigitalBrain.Contracts;

namespace DigitalBrain.Marketplace.Signals;

[GenerateSerializer, Alias("marketplace.earnings-recorded")]
public sealed record EarningsRecorded(
    [property: Id(0)] string EntryId,
    [property: Id(1)] string CreatorId,
    [property: Id(2)] EarningsEntryKind Kind,
    [property: Id(3)] decimal NetUsd,
    [property: Id(4)] DateTimeOffset OccurredAt) : Signal;

[GenerateSerializer, Alias("marketplace.payout-settled")]
public sealed record PayoutSettled(
    [property: Id(0)] string PayoutId,
    [property: Id(1)] string CreatorId,
    [property: Id(2)] decimal AmountUsd,
    [property: Id(3)] bool Paid,
    [property: Id(4)] DateTimeOffset OccurredAt) : Signal;
