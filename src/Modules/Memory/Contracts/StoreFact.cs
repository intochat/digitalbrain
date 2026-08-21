using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.store-fact")]
public sealed record StoreFact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Correlation = null,
    [property: Id(4)] DateTimeOffset? At = null) : RequestSynapse<FactStored>;
