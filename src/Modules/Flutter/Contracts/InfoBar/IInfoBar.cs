using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.InfoBar;

[Alias("infobar"), Orleans.Metadata.DefaultGrainType(UIVocabulary.InfoBarType)]
public interface IInfoBar : INeuron
{
    Task Show(string severity, string title, string body);
    Task Dismiss();
    [ReadOnly, Alias("read")] Task<InfoBarState> Read();
}

[GenerateSerializer, Alias("ui.infobar-state")]
public sealed class InfoBarState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Severity { get; set; } = "info";
    [Id(3)] public string Title { get; set; } = "";
    [Id(4)] public string Body { get; set; } = "";
    [Id(5)] public bool Visible { get; set; }
}