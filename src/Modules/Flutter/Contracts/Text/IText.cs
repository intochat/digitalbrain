using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Text;

[Alias("text"), Orleans.Metadata.DefaultGrainType(UIVocabulary.TextType)]
public interface IText : INeuron
{
    Task Set(string markdown);
    [ReadOnly, Alias("read")] Task<TextState> Read();
}

[GenerateSerializer, Alias("ui.text-state")]
public sealed class TextState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Markdown { get; set; } = "";
}
