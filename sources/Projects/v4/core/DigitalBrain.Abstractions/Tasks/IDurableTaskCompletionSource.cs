using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Tasks;

[Alias("DigitalBrain.Abstractions.Tasks.IDurableTaskCompletionSource")]
public interface IDurableTaskCompletionSource : IGrainWithStringKey
{
    [Alias("SetResultAsync")]
    Task SetResultAsync(string result);

    [Alias("GetResultAsync")]
    [AlwaysInterleave]
    Task<string> GetResultAsync(System.Threading.CancellationToken cancellationToken = default);

    [Alias("GetWaitersCountAsync")]
    Task<int> GetWaitersCountAsync();

    [Alias("RetrieveConfirmedEventsAsync")]
    Task<IReadOnlyList<string>> RetrieveConfirmedEventsAsync();

    [Alias("DeactivateAsync")]
    Task DeactivateAsync();

    [Alias("SetFailedAsync")]
    Task SetFailedAsync(string errorMessage);

    [Alias("SetStatusAsync")]
    Task SetStatusAsync(TaskStatusEnum status);

    [Alias("StartTimeoutAsync")]
    Task StartTimeoutAsync(TimeSpan timeout);

    [Alias("GetStatusAsync")]
    Task<TaskStatusEnum> GetStatusAsync();

    [Alias("InitializeAsync")]
    Task InitializeAsync(string taskType, TimeSpan? timeout = null);

    [Alias("SuspendAsync")]
    Task SuspendAsync();

    [Alias("ResumeAsync")]
    Task ResumeAsync();

    [Alias("RecordRetryAttemptAsync")]
    Task RecordRetryAttemptAsync(int attempt, TimeSpan delay, string exceptionType, string exceptionMessage);

    [Alias("GetTaskDetailsAsync")]
    [AlwaysInterleave]
    Task<DurableTaskDetails> GetTaskDetailsAsync();
}
