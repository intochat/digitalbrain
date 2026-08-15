namespace DigitalBrain.Abstractions.Messaging;

// Why a durable record exists. Author, Correlation and At are composed by the kernel from the
// delivery that requested it — only StatedIntent comes from the caller, so a record can never
// claim to have been created by someone it was not.
[GenerateSerializer]
[Alias("db.provenance")]
public sealed record Provenance(
    [property: Id(0)] NeuronId Author,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] string StatedIntent,
    [property: Id(3)] CorrelationId Correlation);
