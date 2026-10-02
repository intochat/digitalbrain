using Orleans;

namespace DigitalBrain.Core;

[GenerateSerializer]
public sealed class DocumentState
{
    [Id(0)] public bool Written { get; set; }
    [Id(1)] public long Version { get; set; }
    [Id(2)] public string Payload { get; set; } = "";
}
