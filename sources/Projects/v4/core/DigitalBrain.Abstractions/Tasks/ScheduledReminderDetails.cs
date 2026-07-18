using System;
using Orleans;

namespace DigitalBrain.Abstractions.Tasks;

[GenerateSerializer]
[Alias("DigitalBrain.Abstractions.Tasks.ScheduledReminderDetails")]
public sealed class ScheduledReminderDetails
{
    [Id(0)] public string ReminderName { get; set; } = string.Empty;
    [Id(1)] public string CronExpression { get; set; } = string.Empty;
    [Id(2)] public string TaskType { get; set; } = string.Empty;
    [Id(3)] public TimeSpan? TaskTimeout { get; set; }
    [Id(4)] public DurableTaskRetryPolicy? RetryPolicy { get; set; }
    [Id(5)] public bool IsEnabled { get; set; }
    [Id(6)] public DateTime? LastRunTime { get; set; }
    [Id(7)] public DateTime? NextRunTime { get; set; }
}
