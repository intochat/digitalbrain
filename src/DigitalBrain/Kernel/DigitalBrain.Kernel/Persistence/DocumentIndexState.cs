using Orleans;

namespace DigitalBrain.Kernel;

[GenerateSerializer]
public sealed class DocumentIndexState
{
    [Id(0)] public HashSet<string> Ids { get; set; } = new(StringComparer.Ordinal);
}
