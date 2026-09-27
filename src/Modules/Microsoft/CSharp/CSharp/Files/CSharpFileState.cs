namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.file-state")]
internal sealed record CSharpFileState
{
    [Id(0)] public string Source { get; init; } = "";
    [Id(1)] public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
}
