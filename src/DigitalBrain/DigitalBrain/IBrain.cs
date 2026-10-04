namespace DigitalBrain;

[Alias("brain"), Orleans.Metadata.DefaultGrainType("brain")]
public interface IBrain : INeuron
{
    Task<BrainSnapshot> Read();
    Task<BrainSnapshot> Establish(EstablishBrain request);
}

// Published when an established brain wakes: once right after Establish, and again on every
// later activation. Handlers are idempotent; this is the signal the operating system itself
// boots on, and any app can subscribe to it the same way.
[GenerateSerializer, Alias("brain.activated")]
public sealed record Activated(
    [property: Id(0)] string BrainId,
    [property: Id(1)] string OwnerAccountId,
    [property: Id(2)] string Name) : Signal;

[GenerateSerializer, Alias("brain.snapshot")]
public sealed record BrainSnapshot
{
    [Id(0)] public required string BrainId { get; init; }
    [Id(1)] public required string Name { get; init; }
    [Id(2)] public required string OwnerAccountId { get; init; }
    [Id(3)] public DateTimeOffset EstablishedAt { get; init; }
}

[GenerateSerializer, Alias("brain.establish")]
public sealed record EstablishBrain([property: Id(0)] string Name, [property: Id(1)] string OwnerAccountId);
