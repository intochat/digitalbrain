using Ino.Core;

namespace Ino.NeuronTesting;

public sealed record SynapseFire(
    string Type,
    CorrelationId CorrelationId,
    IReadOnlyDictionary<string, string> Args,
    DateTimeOffset FiredAt);
