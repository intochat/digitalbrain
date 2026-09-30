using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Video;

[Alias("video"), Orleans.Metadata.DefaultGrainType(UIVocabulary.VideoType)]
public interface IVideo : INeuron
{
    Task Load(string url, double duration);
    Task Play();
    Task Pause();
    Task Seek(double seconds);
    Task End();
    [ReadOnly, Alias("read")] Task<VideoState> Read();
}

[GenerateSerializer, Alias("ui.video-state")]
public sealed class VideoState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Url { get; set; } = "";
    [Id(3)] public bool Playing { get; set; }
    [Id(4)] public double Position { get; set; }
    [Id(5)] public double Duration { get; set; }
}
