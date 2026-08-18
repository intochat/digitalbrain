namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.fact-memory-state")]
internal sealed class FactMemoryState
{
    [Id(0)]
    public long Watermark { get; set; }

    [Id(1)]
    public List<FactEntry> Facts { get; set; } = [];
}
