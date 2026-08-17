using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.corpus-state")]
internal sealed class CorpusState
{
    [Id(0)]
    public long Watermark { get; set; }

    [Id(1)]
    public List<CorpusEntry> Entries { get; set; } = [];
}
