namespace DigitalBrain.Compute.Storage;

[GenerateSerializer]
internal sealed class ComputeRecordsState
{
    [Id(0)] public string? Root { get; set; }
    // [Id(1)] retired (LegacyImported); never reuse
}
