using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Abstractions.Brain;

// An emission that resolved zero receivers is journaled and never delivered. Without this
// record the loss is invisible: nothing is retried, nothing refuses, nothing is reported.
[GenerateSerializer]
[Alias("db.unrouted")]
public sealed record Unrouted(
    [property: Id(0)] SignalId Delivery,
    [property: Id(1)] string Alias,
    [property: Id(2)] NeuronId Source,
    [property: Id(3)] CorrelationId Correlation) : Signal;
