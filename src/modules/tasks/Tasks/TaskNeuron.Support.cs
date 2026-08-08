using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    private AttemptCursor Cursor(TaskData data) => new(
        Id,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Task '{Id}' has no active Attempt."),
        data.Revision);

    private AttemptRequest Request(TaskData data) => new(
        Id,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Task '{Id}' has no active Attempt."),
        data.Revision,
        data.Goal);

    private bool Matches(TaskData data, AttemptFact fact)
    {
        if (fact.Task != Id
            || fact.Worker != data.Worker
            || fact.Attempt != data.ActiveAttempt)
        {
            return false;
        }

        return fact.Revision == data.Revision;
    }

    private static void AcknowledgePendingDispatch(TaskData data, AttemptFact fact)
    {
        var matches = data.PendingDispatch switch
        {
            AcceptWorkerDispatch { Request: var request } =>
                request.Task == fact.Task
                && request.Worker == fact.Worker
                && request.Attempt == fact.Attempt
                && request.Revision == fact.Revision,
            ContinueWorkerDispatch { Cursor: var cursor } =>
                cursor.Task == fact.Task
                && cursor.Worker == fact.Worker
                && cursor.Attempt == fact.Attempt
                && cursor.Revision == fact.Revision,
            _ => false
        };

        if (matches)
        {
            data.PendingDispatch = null;
        }
    }

    private static TaskSnapshot Snapshot(TaskData data) => new(
        data.Goal,
        data.Worker,
        data.Policy,
        data.State,
        data.Revision,
        data.ActiveAttempt,
        data.Blocker,
        data.Result,
        data.Failure,
        [.. data.Evidence],
        data.RetryOf,
        data.AttemptCount);

    private static void Validate(StartTask command)
    {
        ArgumentNullException.ThrowIfNull(command.Goal);
        ArgumentNullException.ThrowIfNull(command.Policy);

        if (command.Policy.MaximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "A task policy must allow at least one attempt.");
        }

        if (command.Policy.RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "A task retry delay cannot be negative.");
        }

        if (command.Worker == default)
        {
            throw new ArgumentException("A task worker is required.", nameof(command));
        }
    }

    private static void Validate(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("A command id is required.", nameof(commandId));
        }
    }

    private async Task ValidatePredecessorAsync(NeuronId? predecessor)
    {
        if (predecessor is null)
        {
            return;
        }

        if (predecessor == Id || predecessor.Value.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Task '{predecessor}' cannot be the predecessor of Task '{Id}'.");
        }

        var snapshot = await GrainFactory
            .GetGrain<ITask>(predecessor.Value.ToGrainId())
            .Read().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!IsTerminal(snapshot.State))
        {
            throw new InvalidOperationException(
                $"Task '{predecessor}' is not terminal, so Task '{Id}' cannot retry it.");
        }
    }

    private static bool IsTerminal(TaskState state)
        => state is TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled;

    private static bool IsOutcomeUncertain(TaskData data)
        => data.State == TaskState.Waiting && data.Blocker is OutcomeUncertain;
}
