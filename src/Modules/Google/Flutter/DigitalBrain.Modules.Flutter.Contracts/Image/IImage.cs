using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Image;

[Alias("image"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ImageType)]
public interface IImage : INeuron
{
    Task Set(string url, string mediaType, string prompt);
    [ReadOnly, Alias("read")] Task<ImageState> Read();
}

[GenerateSerializer, Alias("ui.image-state")]
public sealed class ImageState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Prompt { get; set; } = "";
    [Id(3)] public string MediaType { get; set; } = "";
    [Id(4)] public string Url { get; set; } = "";
}