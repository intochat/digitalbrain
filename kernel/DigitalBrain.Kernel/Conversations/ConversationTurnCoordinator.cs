namespace DigitalBrain.Kernel;

internal sealed class ConversationTurnCoordinator(
    NeuronDurableState neuronState,
    ConversationDurableState conversationState,
    IConversationRoleInvoker roleInvoker,
    Func<CancellationToken, Task> commit,
    Func<CancellationToken, Task> drainOutbox,
    Func<Task> armRecovery,
    Func<Task> disarmRecovery,
    ConversationActivationGuard activationGuard,
    Action invalidateActivation)
{
    public async Task<ConversationTurnResult> SubmitTurnAsync(
        ConversationTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        activationGuard.EnsureValid();
        ValidateRequest(request);
        var operationId = request.TurnId.Value;

        if (conversationState.Results.TryGetValue(operationId, out var committed))
        {
            EnsureSameIntent(operationId, request);
            return committed;
        }

        if (neuronState.Operations.TryGetValue(operationId, out var existingOperation))
            throw ExistingOperationFailure(existingOperation);

        if (conversationState.Intents.ContainsKey(operationId))
            throw new BrainException(
                NeuronFailureKind.OperationUnknown,
                "The durable turn intent has no reconcilable provider outcome.");

        await ArmRecoveryAsync();
        conversationState.Intents[operationId] =
            request;
        neuronState.Operations[operationId] = new ExternalOperation(
            operationId,
            ExternalOperationStatus.Pending,
            FailureKind: null);
        await CommitOrInvalidateAsync(cancellationToken);

        string response;
        try
        {
            response = await roleInvoker.CompleteAsync(
                request.Role,
                request.Text,
                cancellationToken);
        }
        catch (Exception exception)
        {
            var outcomeUnknown =
                exception is OperationCanceledException ||
                exception is ProviderInvocationException { OutcomeUnknown: true } ||
                exception is HttpRequestException;
            var failureKind = outcomeUnknown
                ? NeuronFailureKind.OperationUnknown
                : exception is BrainException brainException
                    ? brainException.FailureKind
                    : NeuronFailureKind.ProviderUnavailable;
            var transition = outcomeUnknown
                ? (ExternalOperationTransition)new ExternalOperationTransition.Unknown(failureKind)
                : new ExternalOperationTransition.Failed(failureKind);
            neuronState.Operations[operationId] = ExternalOperationTransitions.Apply(
                neuronState.Operations[operationId],
                transition);
            await CommitOrInvalidateAsync(CancellationToken.None);
            await BestEffortDisarmRecoveryAsync();
            throw new BrainException(
                failureKind,
                outcomeUnknown
                    ? "The provider outcome is unknown and cannot be replayed safely."
                    : "The provider operation failed.");
        }

        var revision = checked(conversationState.Revision.Value + 1);
        var result = new ConversationTurnResult(
            request.TurnId,
            request.Role,
            response,
            revision);
        conversationState.Turns[operationId] = new ConversationTurn(
            request.TurnId,
            request.Role,
            request.Text,
            response);
        conversationState.Results[operationId] = result;
        conversationState.Revision.Value = revision;
        neuronState.Operations[operationId] = ExternalOperationTransitions.Apply(
            neuronState.Operations[operationId],
            new ExternalOperationTransition.Succeeded());
        var notification = new NeuronNotification(
            Guid.NewGuid(),
            operationId,
            NotificationDeliveryStatus.Pending,
            AttemptCount: 0);
        neuronState.Outbox[notification.NotificationId] = notification;
        await CommitOrInvalidateAsync(cancellationToken);
        await DrainOutboxAsync(cancellationToken);
        return result;
    }

    public ConversationSnapshot Read()
    {
        activationGuard.EnsureValid();
        var results = conversationState.Results.Values
            .OrderBy(result => result.Revision)
            .ToArray();
        var turns = new ConversationTurn[results.Length];
        for (var index = 0; index < results.Length; index++)
        {
            if (!conversationState.Turns.TryGetValue(
                    results[index].TurnId.Value,
                    out var turn))
                throw new BrainException(
                    NeuronFailureKind.StorageUnavailable,
                    "Committed conversation state is incomplete.");

            turns[index] = turn;
        }

        return new ConversationSnapshot(turns, conversationState.Revision.Value);
    }

    private async Task ArmRecoveryAsync()
    {
        try
        {
            await armRecovery();
        }
        catch (Exception exception)
        {
            throw SanitizeInfrastructureFailure(
                exception,
                NeuronFailureKind.StorageUnavailable,
                "Durable conversation recovery could not be scheduled.");
        }
    }

    private async Task BestEffortDisarmRecoveryAsync()
    {
        if (neuronState.Outbox.Values.Any(
                notification =>
                    notification.DeliveryStatus == NotificationDeliveryStatus.Pending))
            return;

        try
        {
            await disarmRecovery();
        }
        catch
        {
        }
    }

    private async Task DrainOutboxAsync(CancellationToken cancellationToken)
    {
        try
        {
            await drainOutbox(cancellationToken);
        }
        catch (Exception exception)
        {
            throw SanitizeInfrastructureFailure(
                exception,
                NeuronFailureKind.ProviderUnavailable,
                "The committed conversation result is awaiting notification delivery.");
        }
    }

    private async Task CommitOrInvalidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await commit(cancellationToken);
        }
        catch (Exception exception)
        {
            activationGuard.Invalidate();
            try
            {
                invalidateActivation();
            }
            catch
            {
            }

            throw SanitizeInfrastructureFailure(
                exception,
                NeuronFailureKind.StorageUnavailable,
                "Durable conversation state could not be committed.");
        }
    }

    private static BrainException SanitizeInfrastructureFailure(
        Exception exception,
        NeuronFailureKind fallbackFailureKind,
        string detail) =>
        new(
            exception is BrainException brainException
                ? brainException.FailureKind
                : fallbackFailureKind,
            detail);

    private static void ValidateRequest(ConversationTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TurnId.Value == Guid.Empty)
            throw new BrainException(
                NeuronFailureKind.OperationFailed,
                "A non-empty turn id is required.");
        if (!Enum.IsDefined(request.Role))
            throw new BrainException(
                NeuronFailureKind.OperationFailed,
                "A declared conversation role is required.");
        if (string.IsNullOrWhiteSpace(request.Text) ||
            request.Text.Length > ConversationTurnRequest.MaximumTextLength)
            throw new BrainException(
                NeuronFailureKind.OperationFailed,
                "A bounded non-empty turn input is required.");
    }

    private void EnsureSameIntent(
        Guid operationId,
        ConversationTurnRequest request)
    {
        if (!conversationState.Intents.TryGetValue(operationId, out var intent) ||
            intent.TurnId != request.TurnId ||
            intent.Role != request.Role ||
            !string.Equals(intent.Text, request.Text, StringComparison.Ordinal))
            throw new BrainException(
                NeuronFailureKind.OperationFailed,
                "The turn identity is already committed for different input.");
    }

    private static BrainException ExistingOperationFailure(ExternalOperation operation) =>
        operation.Status switch
        {
            ExternalOperationStatus.Pending or ExternalOperationStatus.Unknown =>
                new BrainException(
                    NeuronFailureKind.OperationUnknown,
                    "The provider outcome is unknown and cannot be replayed safely."),
            ExternalOperationStatus.Failed =>
                new BrainException(
                    operation.FailureKind ?? NeuronFailureKind.OperationFailed,
                    "The provider operation already reached a failed final state."),
            ExternalOperationStatus.Succeeded =>
                new BrainException(
                    NeuronFailureKind.OperationUnknown,
                    "The provider operation succeeded without a readable committed result."),
            _ => new BrainException(
                NeuronFailureKind.OperationUnknown,
                "The provider operation state is not reconcilable.")
        };
}
