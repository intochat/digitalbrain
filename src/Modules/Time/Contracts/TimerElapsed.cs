using DigitalBrain.Contracts;
namespace DigitalBrain.Time;
[GenerateSerializer, Alias("time.elapsed")]
public sealed record TimerElapsed(
    [property: Id(0)] string TimerId,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset DueAt,
    [property: Id(3)] DateTimeOffset ObservedAt,
    [property: Id(4)] TimerResolution Resolution,
    [property: Id(5)] string Note) : Signal;
