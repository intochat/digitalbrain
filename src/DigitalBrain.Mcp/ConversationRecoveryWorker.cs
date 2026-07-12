using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

internal sealed record RecoverableConversationCommand(string CommandId, string Prompt);

internal sealed record ConversationRecoveryPlan(
    bool HasPendingOutbox,
    RecoverableConversationCommand? Command);

public sealed class ConversationRecoveryWorker(
    ConversationStateClient conversations,
    ConversationOutboxDispatcher outbox,
    McpInoCommandHandler handler,
    TimeProvider timeProvider)
{
    public async Task<bool> RecoverAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken = default)
    {
        context = await conversations.ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = conversations.Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var expectedIdentity = new ConversationIdentity(
            context.TenantId,
            context.WorkspaceId,
            context.Principal,
            context.ConversationId!);
        if (state.Identity is not null && state.Identity != expectedIdentity)
            throw new UnauthorizedAccessException("Conversation recovery scope denied.");

        var plan = Plan(state, timeProvider.GetUtcNow());
        var recovered = false;
        if (plan.HasPendingOutbox)
        {
            await outbox.DispatchAsync(context, cancellationToken).ConfigureAwait(false);
            recovered = true;
        }

        if (plan.Command is not { } command ||
            !context.Grants.Overlaps(["brain.interact", "ui.action"])) return recovered;

        var commandContext = context with
        {
            IdempotencyKey = command.CommandId,
            Grants = context.Grants.Append("brain.interact").ToHashSet(StringComparer.Ordinal),
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        using var executionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionTimeout.CancelAfter(TimeSpan.FromMinutes(2));
        await handler.ExecuteAsync(
            new CommandEnvelope(
                McpInoCommandHandler.CommandType,
                2,
                command.CommandId,
                commandContext,
                JsonSerializer.SerializeToElement(new { prompt = command.Prompt })),
            executionTimeout.Token).ConfigureAwait(false);
        return true;
    }

    internal static ConversationRecoveryPlan Plan(ConversationState state, DateTimeOffset now)
    {
        var hasPendingOutbox = state.Outbox.Any(static entry => entry.DispatchedAt is null);
        if (state.Identity is null || state.Lifecycle == ConversationLifecycle.Tombstoned)
            return new(hasPendingOutbox, null);

        var operation = state.Operations.FirstOrDefault(static candidate => candidate.Status is not (
            ConversationOperationStatus.Succeeded or ConversationOperationStatus.Failed or
            ConversationOperationStatus.OutcomeUnknown or ConversationOperationStatus.Cancelled));
        var recoverable = operation?.Status == ConversationOperationStatus.Pending ||
                          operation?.Status == ConversationOperationStatus.RetryScheduled &&
                          operation.NextAttemptAt is { } due && due <= now;
        if (!recoverable) return new(hasPendingOutbox, null);

        var userTurn = state.Turns.LastOrDefault(turn =>
            turn.Kind == ConversationTurnKind.User &&
            string.Equals(turn.OperationId, operation!.OperationId, StringComparison.Ordinal));
        if (userTurn is null)
            throw new RuntimeStateIntegrityException("recoverable conversation command is missing its user turn");
        return new(hasPendingOutbox, new(operation!.CommandId, userTurn.Text));
    }
}
