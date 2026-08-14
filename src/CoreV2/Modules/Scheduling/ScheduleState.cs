namespace Brain.Modules.Scheduling;

[GenerateSerializer]
public sealed class ScheduleState
{
    [Id(0)]
    public string ScheduleId { get; set; } = string.Empty;

    [Id(1)]
    public string? Title { get; set; }

    [Id(2)]
    public DateTimeOffset? DueAtUtc { get; set; }

    [Id(3)]
    public ScheduleLifecycle Status { get; set; }

    [Id(4)]
    public DateTimeOffset? TriggeredAtUtc { get; set; }

    [Id(5)]
    public HashSet<string> ProcessedRequests { get; set; } = new(StringComparer.Ordinal);
}
