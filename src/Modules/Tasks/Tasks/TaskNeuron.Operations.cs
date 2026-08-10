using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    public async Task HandleAsync(ReadTaskOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var data = Load();
        RequireActiveAttempt(data, synapse.Attempt);
        ValidateSequence(synapse.Sequence);

        var operations = Operations(data);
        var key = OperationKey(synapse.Attempt, synapse.Sequence);
        operations.TryGetValue(key, out var operation);

        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(new ReadTaskOperationResult(operation), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(PrepareTaskOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var data = Load();
        RequireActiveAttempt(data, synapse.Attempt);
        ValidateSequence(synapse.Sequence);
        ValidateEdge(synapse.Edge);
        ValidateReference(synapse.RequestPayload, nameof(synapse.RequestPayload));

        var operations = Operations(data);
        var key = OperationKey(synapse.Attempt, synapse.Sequence);

        if (operations.TryGetValue(key, out var existing))
        {
            if (!EdgesEqual(existing.Edge, synapse.Edge))
            {
                throw new InvalidOperationException(
                    $"Task '{Id}' operation {synapse.Sequence} on attempt '{synapse.Attempt.Value:N}' already exists with a different edge.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(existing, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        RequireNextSequence(operations, synapse.Attempt, synapse.Sequence);

        var snapshot = new TaskOperationSnapshot(
            synapse.Attempt,
            synapse.Sequence,
            synapse.Edge,
            synapse.RequestPayload,
            TaskOperationPhase.Prepared,
            ResponsePayload: null,
            RedactedSummary: null);

        operations[key] = snapshot;
        data.Operations = operations;
        cancellationToken.ThrowIfCancellationRequested();
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(TransitionTaskOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var data = Load();
        RequireActiveAttempt(data, synapse.Attempt);
        ValidateSequence(synapse.Sequence);

        var operations = Operations(data);
        var key = OperationKey(synapse.Attempt, synapse.Sequence);
        if (!operations.TryGetValue(key, out var existing))
        {
            throw new InvalidOperationException(
                $"Task '{Id}' has no operation at sequence {synapse.Sequence} for attempt '{synapse.Attempt.Value:N}'.");
        }

        if (existing.Phase == synapse.Phase
            && existing.ResponsePayload == synapse.ResponsePayload
            && existing.RedactedSummary == synapse.RedactedSummary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(existing, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (existing.Phase != synapse.ExpectedPhase)
        {
            throw new InvalidOperationException(
                $"Task '{Id}' operation {synapse.Sequence} is in phase '{existing.Phase}', not expected '{synapse.ExpectedPhase}'.");
        }

        ValidateTransition(existing.Phase, synapse.Phase, synapse.ResponsePayload);

        var snapshot = existing with
        {
            Phase = synapse.Phase,
            ResponsePayload = synapse.ResponsePayload,
            RedactedSummary = synapse.RedactedSummary,
        };

        operations[key] = snapshot;
        data.Operations = operations;

        if (synapse.Phase == TaskOperationPhase.Uncertain)
        {
            var blockerId = OperationBlockerId(Id, synapse.Attempt, synapse.Sequence);
            data.State = TaskState.Waiting;
            data.Blocker = new OutcomeUncertain(blockerId);
            data.PendingDispatch = null;

            cancellationToken.ThrowIfCancellationRequested();
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(new AttemptOutcomeUncertain(
                Id,
                data.Worker,
                synapse.Attempt,
                data.Revision,
                blockerId)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static Dictionary<string, TaskOperationSnapshot> Operations(TaskData data)
        => data.Operations ?? new Dictionary<string, TaskOperationSnapshot>(StringComparer.Ordinal);

    private static string OperationKey(AttemptId attempt, int sequence)
        => $"{attempt.Value:N}:{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static void RequireActiveAttempt(TaskData data, AttemptId attempt)
    {
        if (IsTerminal(data.State))
        {
            throw new InvalidOperationException("A terminal task cannot accept operation commands.");
        }

        if (data.ActiveAttempt is null)
        {
            throw new InvalidOperationException("A task with no active attempt cannot accept operation commands.");
        }

        if (data.ActiveAttempt != attempt)
        {
            throw new InvalidOperationException(
                $"Operation attempt '{attempt.Value:N}' does not match active attempt '{data.ActiveAttempt.Value.Value:N}'.");
        }
    }

    private static void ValidateSequence(int sequence)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Operation sequence must be non-negative.");
        }
    }

    private static void RequireNextSequence(
        Dictionary<string, TaskOperationSnapshot> operations,
        AttemptId attempt,
        int sequence)
    {
        var next = 0;
        foreach (var snapshot in operations.Values)
        {
            if (snapshot.Attempt == attempt && snapshot.Sequence >= next)
            {
                next = snapshot.Sequence + 1;
            }
        }

        if (sequence != next)
        {
            throw new InvalidOperationException(
                $"Operation sequence must be contiguous; expected {next}, received {sequence}.");
        }
    }

    private static void ValidateEdge(TaskOperationEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (edge.Target == default
            || string.IsNullOrWhiteSpace(edge.Target.Type)
            || string.IsNullOrWhiteSpace(edge.Target.Name))
        {
            throw new ArgumentException("Operation edge requires a non-default target neuron id.", nameof(edge));
        }

        if (string.IsNullOrWhiteSpace(edge.RequestSynapseId))
        {
            throw new ArgumentException("Operation edge requires a request synapse id.", nameof(edge));
        }

        if (edge.RequestSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                edge.RequestSchemaVersion,
                "Request schema version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(edge.ResponseSynapseId))
        {
            throw new ArgumentException("Operation edge requires a response synapse id.", nameof(edge));
        }

        if (edge.ResponseSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                edge.ResponseSchemaVersion,
                "Response schema version must be positive.");
        }
    }

    private static void ValidateReference(ProtectedPayloadReference reference, string paramName)
    {
        if (reference.Id == Guid.Empty)
        {
            throw new ArgumentException("Protected payload reference cannot be empty.", paramName);
        }
    }

    private static void ValidateTransition(
        TaskOperationPhase current,
        TaskOperationPhase target,
        ProtectedPayloadReference? responsePayload)
    {
        switch (current, target)
        {
            case (TaskOperationPhase.Prepared, TaskOperationPhase.Dispatched):
                if (responsePayload is not null)
                {
                    throw new InvalidOperationException("Prepared→Dispatched cannot carry a response reference.");
                }

                break;

            case (TaskOperationPhase.Dispatched, TaskOperationPhase.Completed):
                if (responsePayload is null || responsePayload.Value.Id == Guid.Empty)
                {
                    throw new InvalidOperationException("Dispatched→Completed requires a non-empty response reference.");
                }

                break;

            case (TaskOperationPhase.Dispatched, TaskOperationPhase.Uncertain):
                if (responsePayload is not null)
                {
                    throw new InvalidOperationException("Dispatched→Uncertain cannot carry a response reference.");
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Transition from '{current}' to '{target}' is not allowed.");
        }
    }

    private static bool EdgesEqual(TaskOperationEdge left, TaskOperationEdge right)
        => left.Target == right.Target
            && string.Equals(left.RequestSynapseId, right.RequestSynapseId, StringComparison.Ordinal)
            && left.RequestSchemaVersion == right.RequestSchemaVersion
            && string.Equals(left.ResponseSynapseId, right.ResponseSynapseId, StringComparison.Ordinal)
            && left.ResponseSchemaVersion == right.ResponseSchemaVersion;

    private static BlockerId OperationBlockerId(NeuronId task, AttemptId attempt, int sequence)
    {
        var material = $"{task}:{attempt.Value:N}:{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var guidBytes = hash.AsSpan(0, 16).ToArray();
        if (guidBytes.All(b => b == 0))
        {
            guidBytes[^1] = 1;
        }

        return new BlockerId(new Guid(guidBytes));
    }
}
