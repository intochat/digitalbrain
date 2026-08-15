using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

internal sealed class ExecutionRuntime(ExecutionNeuron neuron, ExecutionStateStore state)
{
    internal NeuronId Id => neuron.Id;

    internal IGrainFactory GrainFactory => neuron.ExecutionGrainFactory;

    internal TimeProvider TimeProvider => neuron.ExecutionTimeProvider;

    internal bool HasStarted => state.HasStarted;

    internal ExecutionData Load() => state.Load(Id);

    internal ExecutionData? LoadIfStarted() => state.LoadIfStarted();

    internal void Stage(ExecutionData data) => state.Stage(data);

    internal void StageForTurn(ExecutionData data)
        => state.StageForTurn(data, neuron.EnlistExecutionRollback);

    internal Task SaveAsync(ExecutionData data)
        => state.SaveAsync(data, neuron.WriteExecutionStateAsync);

    internal Task<IGrainReminder> RegisterReminderAsync(
        string name,
        TimeSpan dueTime,
        TimeSpan period)
        => neuron.RegisterExecutionReminderAsync(name, dueTime, period);

    internal Task UnregisterReminderAsync(string name)
        => neuron.UnregisterExecutionReminderAsync(name);

    internal Task<SynapseDelivery> SendAsync(NeuronId receiver, Synapse synapse)
        => neuron.SendFromExecutionAsync(receiver, synapse);

    internal Task EmitAsync(Synapse synapse)
        => neuron.EmitFromExecutionAsync(synapse);

    internal void DelayDeactivation(TimeSpan duration)
        => neuron.DelayExecutionDeactivation(duration);

    internal Task NotifyOriginOfStateAsync(ExecutionData data)
    {
        if (data.Origin is not { } origin || origin == default)
        {
            return Task.CompletedTask;
        }

        if (!ExecutionModel.IsTerminal(data.State) && data.State != ExecutionState.Waiting)
        {
            return Task.CompletedTask;
        }

        return SendAsync(
            origin,
            new ExecutionTerminal(Id, data.State, data.Revision, data.Result, data.Failure));
    }
}

internal static class ExecutionReminders
{
    internal const string Retry = "db.execution.retry";
    internal const string Dispatch = "db.execution.dispatch";
    internal static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
}
