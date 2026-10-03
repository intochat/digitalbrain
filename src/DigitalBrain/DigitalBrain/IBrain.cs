namespace DigitalBrain;

[Alias("brain"), Orleans.Metadata.DefaultGrainType("brain")]
public interface IBrain : INeuron
{
    Task<BrainSnapshot> Read();
    Task<BrainSnapshot> Establish(EstablishBrain request);
}

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
