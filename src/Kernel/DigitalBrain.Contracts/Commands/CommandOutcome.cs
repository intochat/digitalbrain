using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-outcome")]
public sealed record CommandOutcome(
    [property: Id(0)] CommandPhase Phase,
    [property: Id(1)] int Incarnation,
    [property: Id(2)] NeuronId Caller,
    [property: Id(3)] string Interface,
    [property: Id(4)] string Method,
    [property: Id(5)] string ArgumentsHash,
    [property: Id(6)] string? ResultJson,
    [property: Id(7)] string? Error,
    [property: Id(8)] long Sequence)
{
    public bool Matches(NeuronId caller, string interfaceAlias, string method, string argumentsHash)
        => Caller == caller
            && StringComparer.Ordinal.Equals(Interface, interfaceAlias)
            && StringComparer.Ordinal.Equals(Method, method)
            && StringComparer.Ordinal.Equals(ArgumentsHash, argumentsHash);
}
