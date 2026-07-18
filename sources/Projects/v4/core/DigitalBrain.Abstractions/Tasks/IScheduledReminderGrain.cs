using System;
using System.Threading.Tasks;
using Orleans;

namespace DigitalBrain.Abstractions.Tasks;

[Alias("DigitalBrain.Abstractions.Tasks.IScheduledReminderGrain")]
public interface IScheduledReminderGrain : IGrainWithStringKey
{
    [Alias("ScheduleAsync")]
    Task ScheduleAsync(
        string cronExpression, 
        string taskType, 
        TimeSpan? taskTimeout = null, 
        DurableTaskRetryPolicy? retryPolicy = null);

    [Alias("UnscheduleAsync")]
    Task UnscheduleAsync();

    [Alias("GetDetailsAsync")]
    Task<ScheduledReminderDetails> GetDetailsAsync();

    [Alias("TriggerAsync")]
    Task TriggerAsync();
}
