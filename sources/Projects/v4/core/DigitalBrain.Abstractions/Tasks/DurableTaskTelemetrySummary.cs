using System.Collections.Generic;
using Orleans;

namespace DigitalBrain.Abstractions.Tasks;

[GenerateSerializer]
[Alias("DigitalBrain.Abstractions.Tasks.DurableTaskTelemetrySummary")]
public sealed class DurableTaskTelemetrySummary
{
    [Id(0)] public int InFlightTasksCount { get; set; }
    [Id(1)] public Dictionary<TaskStatusEnum, int> InFlightTasksByStatus { get; set; } = new();
    [Id(2)] public Dictionary<string, int> InFlightTasksByType { get; set; } = new();
    [Id(3)] public long TotalCompletedTasks { get; set; }
    [Id(4)] public long TotalFailedTasks { get; set; }
    [Id(5)] public int ActiveRemindersCount { get; set; }
}
