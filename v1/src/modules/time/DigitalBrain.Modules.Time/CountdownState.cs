using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.countdown-state")]
internal sealed class CountdownState(
    CountdownStatus status,
    long generation,
    long revision,
    NeuronId destination,
    DateTimeOffset scheduledAt,
    DateTimeOffset dueAt,
    TimeSpan duration,
    Dictionary<CommandId, CountdownSnapshot> receipts,
    bool occurrenceCommitted,
    string? activeReminderName)
{
    [Id(0)]
    public CountdownStatus Status { get; set; } = status;

    [Id(1)]
    public long Generation { get; set; } = generation;

    [Id(2)]
    public long Revision { get; set; } = revision;

    [Id(3)]
    public NeuronId Destination { get; set; } = destination;

    [Id(4)]
    public DateTimeOffset ScheduledAt { get; set; } = scheduledAt;

    [Id(5)]
    public DateTimeOffset DueAt { get; set; } = dueAt;

    [Id(6)]
    public TimeSpan Duration { get; set; } = duration;

    [Id(7)]
    public Dictionary<CommandId, CountdownSnapshot> Receipts { get; set; } = receipts;

    [Id(8)]
    public bool OccurrenceCommitted { get; set; } = occurrenceCommitted;

    [Id(9)]
    public string? ActiveReminderName { get; set; } = activeReminderName;
}
