namespace DigitalBrain.Execution;

internal sealed class ExecutionDispatcher
{
    private readonly ExecutionDispatchQueue _queue;
    private readonly ExecutionRecoveryHandler _recovery;

    internal ExecutionDispatcher(ExecutionRuntime runtime)
    {
        _queue = new ExecutionDispatchQueue(runtime);
        _recovery = new ExecutionRecoveryHandler(runtime, _queue);
    }

    internal Task<IGrainReminder> RegisterDispatchReminderAsync()
        => _queue.RegisterReminderAsync();

    internal Task UnregisterReminderAsync(string reminderName)
        => _queue.UnregisterReminderAsync(reminderName);

    internal Task TryDispatchPendingAsync()
        => _queue.TryDispatchPendingAsync();

    internal Task StagePendingDispatchForTurnAsync()
        => _queue.StagePendingDispatchForTurnAsync();

    internal Task RecoverAfterActivationAsync()
        => _recovery.RecoverAfterActivationAsync();

    internal Task ReceiveReminderAsync(string reminderName)
        => _recovery.ReceiveReminderAsync(reminderName);
}
