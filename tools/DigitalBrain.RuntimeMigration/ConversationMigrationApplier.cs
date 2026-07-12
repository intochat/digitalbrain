using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.RuntimeMigration;

public sealed class ConversationMigrationApplier(IClusterClient cluster)
{
    public async Task<string> ApplyAndVerifyAsync(
        RuntimeMigrationPlan plan,
        CancellationToken cancellationToken = default)
    {
        var work = new List<ConversationWork>(plan.Conversations.Count);
        foreach (var conversation in plan.Conversations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neuron = cluster.GetGrain<IConversationNeuron>(conversation.GrainKey);
            var script = BuildScript(conversation);
            var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var prefix = FindPrefix(script, state);
            if (prefix < 0) throw new MigrationGapException("destination-state-conflict");
            work.Add(new ConversationWork(conversation, neuron, state, prefix));
        }

        foreach (var item in work)
        {
            var script = BuildScript(item.Plan);
            var state = item.State;
            for (var stepIndex = item.Prefix; stepIndex < script.Steps.Count; stepIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expected = script.States[stepIndex + 1];
                state = await ApplyStepAsync(
                    item.Neuron,
                    script.Steps[stepIndex],
                    state,
                    expected,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var verified = new List<string>(work.Count);
        foreach (var item in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await item.Neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!SameState(state, ExpectedState(item.Plan)))
                throw new MigrationGapException("destination-readback-mismatch");
            var digest = MigrationHash.ConversationDigest(state);
            if (!FixedTimeDigestEquals(digest, item.Plan.ExpectedDigest))
                throw new MigrationGapException("destination-content-mismatch");
            verified.Add(item.Plan.GrainKey + ":" + digest);
        }
        var aggregateDigest = MigrationHash.Sha256(string.Join('\n',
            verified.Prepend("digitalbrain-runtime-migration-destination-v1")));
        if (!FixedTimeDigestEquals(aggregateDigest, plan.ExpectedDigest))
            throw new MigrationGapException("destination-aggregate-mismatch");
        return aggregateDigest;
    }

    public static ConversationState ExpectedState(ConversationImportPlan plan) => BuildScript(plan).States[^1];

    internal static IReadOnlyList<ConversationState> ExpectedStates(ConversationImportPlan plan) =>
        BuildScript(plan).States;

    internal static int ResumeIndex(ConversationImportPlan plan, ConversationState state) =>
        FindPrefix(BuildScript(plan), state);

    private static ConversationScript BuildScript(ConversationImportPlan plan)
    {
        var steps = new List<MigrationStep>();
        Add(
            state => ConversationTransitions.Initialize(state, state.Revision, plan.Identity),
            (neuron, state, _) => neuron.InitializeAsync(state.Revision, plan.Identity));

        var operations = plan.Operations.ToDictionary(
            static operation => operation.Destination.OperationId,
            StringComparer.Ordinal);
        foreach (var turn in plan.Turns)
        {
            if (!operations.TryGetValue(turn.OperationId, out var operation))
                throw new MigrationGapException("migration-plan-invalid");
            if (turn.Role == "user")
            {
                Add(
                    state => ConversationTransitions.BeginOperation(
                        state,
                        state.Revision,
                        operation.Destination.CommandId,
                        operation.InputHash,
                        operation.Destination.OperationId,
                        turn.Text,
                        turn.CreatedAt),
                    (neuron, state, _) => neuron.BeginOperationAsync(
                        state.Revision,
                        operation.Destination.CommandId,
                        operation.InputHash,
                        operation.Destination.OperationId,
                        turn.Text,
                        turn.CreatedAt));
                continue;
            }
            if (turn.Role != "assistant") throw new MigrationGapException("migration-plan-invalid");
            var staging = operation.Destination with
            {
                Status = ConversationOperationStatus.OutcomeUnknown,
                NextAttemptAt = null,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                TerminalPolicy = ConversationTerminalPolicy.ManualIntervention,
                SafeReason = "legacy-migration-staging",
                SuspendedInvocation = null,
                UpdatedAt = turn.CreatedAt
            };
            Add(
                state => ConversationTransitions.PutOperation(state, state.Revision, staging),
                (neuron, state, _) => neuron.PutOperationAsync(state.Revision, staging));
            Add(
                state => ConversationTransitions.AppendAssistantTurn(
                    state,
                    state.Revision,
                    operation.Destination.OperationId,
                    turn.Text,
                    turn.CreatedAt),
                (neuron, state, _) => neuron.AppendAssistantTurnAsync(
                    state.Revision,
                    operation.Destination.OperationId,
                    turn.Text,
                    turn.CreatedAt));
        }

        foreach (var operation in plan.Operations)
        {
            var destination = operation.Destination;
            if (destination.Status == ConversationOperationStatus.AwaitingAuthorization)
            {
                var invocation = destination.SuspendedInvocation ??
                                 throw new MigrationGapException("migration-plan-invalid");
                var pending = destination with
                {
                    Status = ConversationOperationStatus.Pending,
                    NextAttemptAt = null,
                    LeaseOwner = null,
                    LeaseExpiresAt = null,
                    TerminalPolicy = ConversationTerminalPolicy.VerifyBeforeRetry,
                    SafeReason = destination.SafeReason,
                    SuspendedInvocation = null
                };
                Add(
                    state => ConversationTransitions.PutOperation(state, state.Revision, pending),
                    (neuron, state, _) => neuron.PutOperationAsync(state.Revision, pending));
                Add(
                    state => ConversationTransitions.SuspendAuthorization(
                        state,
                        state.Revision,
                        destination.OperationId,
                        invocation,
                        destination.UpdatedAt),
                    (neuron, state, _) => neuron.SuspendAuthorizationAsync(
                        state.Revision,
                        destination.OperationId,
                        invocation,
                        destination.UpdatedAt));
            }
            else
            {
                Add(
                    state => ConversationTransitions.PutOperation(state, state.Revision, destination),
                    (neuron, state, _) => neuron.PutOperationAsync(state.Revision, destination));
            }
        }
        Add(
            state => ConversationTransitions.RecordMigration(state, state.Revision, plan.MigrationId),
            (neuron, state, _) => neuron.RecordMigrationAsync(state.Revision, plan.MigrationId));

        var states = new List<ConversationState>(steps.Count + 1) { ConversationState.Empty() };
        foreach (var step in steps) states.Add(step.Transition(states[^1]));
        return new ConversationScript(steps, states);

        void Add(
            Func<ConversationState, ConversationState> transition,
            Func<IConversationNeuron, ConversationState, CancellationToken, Task<ConversationState>> apply) =>
            steps.Add(new MigrationStep(transition, apply));
    }

    private static int FindPrefix(ConversationScript script, ConversationState state)
    {
        for (var index = script.States.Count - 1; index >= 0; index--)
            if (SameState(state, script.States[index])) return index;
        return -1;
    }

    private static async Task<ConversationState> ApplyStepAsync(
        IConversationNeuron neuron,
        MigrationStep step,
        ConversationState current,
        ConversationState expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await step.Apply(neuron, current, cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!SameState(result, expected))
                throw new MigrationGapException("destination-write-mismatch");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MigrationGapException)
        {
            throw;
        }
        catch
        {
            ConversationState readback;
            try { readback = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { throw new MigrationGapException("destination-write-ambiguous"); }
            if (SameState(readback, expected)) return readback;
            throw new MigrationGapException("destination-write-ambiguous");
        }
    }

    private static bool SameState(ConversationState first, ConversationState second)
    {
        var firstDigest = StateDigest(first);
        var secondDigest = StateDigest(second);
        return FixedTimeDigestEquals(firstDigest, secondDigest);
    }

    private static string StateDigest(ConversationState state) =>
        MigrationHash.Sha256(JsonSerializer.SerializeToUtf8Bytes(state));

    private static bool FixedTimeDigestEquals(string first, string second) =>
        first.Length == 64 && second.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));

    private sealed record MigrationStep(
        Func<ConversationState, ConversationState> Transition,
        Func<IConversationNeuron, ConversationState, CancellationToken, Task<ConversationState>> Apply);
    private sealed record ConversationScript(
        IReadOnlyList<MigrationStep> Steps,
        IReadOnlyList<ConversationState> States);
    private sealed record ConversationWork(
        ConversationImportPlan Plan,
        IConversationNeuron Neuron,
        ConversationState State,
        int Prefix);
}
