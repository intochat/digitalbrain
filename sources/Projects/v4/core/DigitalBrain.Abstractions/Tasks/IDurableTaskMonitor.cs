using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace DigitalBrain.Abstractions.Tasks;

[GenerateSerializer]
[Alias("DigitalBrain.Abstractions.Tasks.DurableTaskInfo")]
public sealed class DurableTaskInfo
{
    [Id(0)] public string TaskId { get; set; } = string.Empty;
    [Id(1)] public string TaskType { get; set; } = string.Empty;
    [Id(2)] public TaskStatusEnum Status { get; set; }
    [Id(3)] public string Result { get; set; } = string.Empty;
    [Id(4)] public string ErrorMessage { get; set; } = string.Empty;
}

[GenerateSerializer]
[Alias("DigitalBrain.Abstractions.Tasks.ScheduledReminderInfo")]
public sealed class ScheduledReminderInfo
{
    [Id(0)] public string ReminderName { get; set; } = string.Empty;
    [Id(1)] public string CronExpression { get; set; } = string.Empty;
    [Id(2)] public string TaskType { get; set; } = string.Empty;
    [Id(3)] public bool IsEnabled { get; set; }
    [Id(4)] public DateTime? LastRunTime { get; set; }
    [Id(5)] public DateTime? NextRunTime { get; set; }
}

[Alias("DigitalBrain.Abstractions.Tasks.IDurableTaskMonitor")]
public interface IDurableTaskMonitor : IGrainWithStringKey
{
    [Alias("RegisterTaskAsync")]
    Task RegisterTaskAsync(string taskId, string taskType);

    [Alias("GetTasksAsync")]
    Task<IReadOnlyList<DurableTaskInfo>> GetTasksAsync();

    [Alias("RegisterReminderAsync")]
    Task RegisterReminderAsync(string reminderName, string cronExpression, string taskType);

    [Alias("UnregisterReminderAsync")]
    Task UnregisterReminderAsync(string reminderName);

    [Alias("GetRemindersAsync")]
    Task<IReadOnlyList<ScheduledReminderInfo>> GetRemindersAsync();

    [Alias("UpdateTaskStatusAsync")]
    Task UpdateTaskStatusAsync(string taskId, TaskStatusEnum status, string? result = null, string? errorMessage = null);

    [Alias("UpdateReminderStatusAsync")]
    Task UpdateReminderStatusAsync(string reminderName, bool isEnabled, DateTime? lastRunTime, DateTime? nextRunTime);

    [Alias("GetInFlightTasksAsync")]
    Task<IReadOnlyList<DurableTaskInfo>> GetInFlightTasksAsync();

    [Alias("GetTelemetrySummaryAsync")]
    Task<DurableTaskTelemetrySummary> GetTelemetrySummaryAsync();
}
