using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Brain;

// Compatibility codec for facts written by the pre-v2 outcome rail. Keep until a durable
// journal migration proves that no retained payload uses this alias.
[GenerateSerializer]
[Alias("db.unrouted")]
internal sealed record LegacyUndeliveredSignal(
    [property: Id(0)] SignalId Delivery,
    [property: Id(1)] string Alias,
    [property: Id(2)] NeuronId Source,
    [property: Id(3)] CorrelationId Correlation) : Signal;
