namespace DigitalBrain.Kernel;

[GenerateSerializer, Alias("brain.state")]
internal sealed record BrainState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public string OwnerAccountId { get; set; } = "";
    [Id(2)] public DateTimeOffset EstablishedAt { get; set; }
}
