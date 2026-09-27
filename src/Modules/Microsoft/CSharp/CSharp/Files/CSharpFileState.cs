namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.file-state")]
internal sealed class CSharpFileState
{
    [Id(0)] public string Source { get; set; } = "";
    [Id(1)] public Dictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}
