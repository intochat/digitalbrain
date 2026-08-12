using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Corpus;

[GenerateSerializer]
[Alias("db.corpus-state")]
internal sealed class CorpusState
{
    [Id(0)]
    public long Watermark { get; set; }

    [Id(1)]
    public List<CorpusEntry> Entries { get; set; } = [];
}
