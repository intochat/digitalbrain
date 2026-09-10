namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-generation")]
public sealed record TimerGeneration([property: Id(0)] long Value);
