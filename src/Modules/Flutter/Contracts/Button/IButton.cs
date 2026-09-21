using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Button;

[Alias("button"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ButtonType)]
public interface IButton : INeuron
{
    Task Set(string label, string action, bool enabled = true);
    Task Click();
    [ReadOnly, Alias("read")] Task<ButtonState> Read();
}

[GenerateSerializer, Alias("ui.button-state")]
public sealed class ButtonState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public string Action { get; set; } = "";
    [Id(4)] public bool Enabled { get; set; } = true;
    [Id(5)] public int ClickCount { get; set; }
}