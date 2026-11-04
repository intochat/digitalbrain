namespace DigitalBrain.Time;

/// <summary>A schedule fired. Durable, so a restart can tell which occurrences already ran.</summary>
/// <param name="Schedule">The schedule text, for example <c>07:00</c> or <c>every 5m</c>.</param>
/// <param name="At">The occurrence time.</param>
[Signal("time.tick")]
public sealed record Tick(string Schedule, DateTimeOffset At) : Signal;
