namespace DigitalBrain.Time;
[GenerateSerializer, Alias("time.state")]
internal sealed record TimerState
{
    [Id(0)] public TimerStatus Status { get; init; }
    [Id(1)] public long Generation { get; init; }
    [Id(2)] public DateTimeOffset? ScheduledAt { get; init; }
    [Id(3)] public DateTimeOffset? DueAt { get; init; }
    [Id(4)] public int? DurationSeconds { get; init; }
    [Id(5)] public string? Note { get; init; }
    public TimerSnapshot Snapshot() => new(Status, Generation, ScheduledAt, DueAt, DurationSeconds, Note);
}
