using DigitalBrain.Contracts;
namespace DigitalBrain.Time.Timers.Signals;

[GenerateSerializer, Alias("time.timer-tick")]
public sealed record TimerTick(
    [property: Id(0)] string TimerId,
    [property: Id(1)] DateTimeOffset ObservedAt) : Signal;
