using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Rating;

[Alias("rating"), Orleans.Metadata.DefaultGrainType(UIVocabulary.RatingType)]
public interface IRating : INeuron
{
    Task Set(int max, int value);
    [ReadOnly, Alias("read")] Task<RatingState> Read();
}

[GenerateSerializer, Alias("ui.rating-state")]
public sealed class RatingState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public int Max { get; set; } = 5;
    [Id(3)] public int Value { get; set; }
}
