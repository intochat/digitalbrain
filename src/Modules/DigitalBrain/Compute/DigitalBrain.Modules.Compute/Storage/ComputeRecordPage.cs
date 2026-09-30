namespace DigitalBrain.Compute.Storage;

[GenerateSerializer]
internal sealed record ComputeRecordPage([property: Id(0)] ComputeStoredRecord[] Items, [property: Id(1)] string? NextKey);
