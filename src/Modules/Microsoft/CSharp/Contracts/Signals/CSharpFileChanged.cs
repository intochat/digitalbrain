using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.file-changed")]
public sealed record CSharpFileChanged(
    [property: Id(0)] string FileId,
    [property: Id(1)] CSharpFileStatus Status) : Signal;
