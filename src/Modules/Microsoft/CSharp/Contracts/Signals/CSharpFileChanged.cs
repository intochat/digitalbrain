using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.file-changed")]
public sealed record CSharpFileChanged([property: Id(0)] string FileId) : Signal;
