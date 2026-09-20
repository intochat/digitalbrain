using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.TextField;

[Alias("textfield"), Orleans.Metadata.DefaultGrainType(UIVocabulary.TextFieldType)]
public interface ITextField : INeuron
{
    Task Configure(string label, string kind);
    Task SetValue(string value);
    [ReadOnly, Alias("read")] Task<TextFieldState> Read();
}

[GenerateSerializer, Alias("ui.textfield-state")]
public sealed class TextFieldState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public string Kind { get; set; } = "text";
    [Id(4)] public string Value { get; set; } = "";
}
