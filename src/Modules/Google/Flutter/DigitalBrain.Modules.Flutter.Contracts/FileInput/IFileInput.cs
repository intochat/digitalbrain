using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.FileInput;

[Alias("fileinput"), Orleans.Metadata.DefaultGrainType(UIVocabulary.FileInputType)]
public interface IFileInput : INeuron
{
    Task Configure(string label);
    Task Capture(string name, string content);
    [ReadOnly, Alias("read")] Task<FileInputState> Read();
}

[GenerateSerializer, Alias("ui.fileinput-state")]
public sealed class FileInputState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public string Label { get; set; } = "";
}

[GenerateSerializer, Alias("ui.file-captured")]
public sealed record FileCaptured([property: Id(0)] string Name, [property: Id(1)] string FileName, [property: Id(2)] string Content) : Signal;

[GenerateSerializer, Alias("ui.fileinput-changed")]
public sealed record FileInputChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;
