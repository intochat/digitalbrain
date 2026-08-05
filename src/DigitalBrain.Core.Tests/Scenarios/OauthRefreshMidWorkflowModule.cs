namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record ExpenseWorkflowStart(string ExpenseId, string ReceiptMessageId) : Synapse;

public sealed record ExpenseGmailFetched(string ExpenseId, string BlobRef) : Synapse;

public sealed record ExpenseDriveUploadAsked(string ExpenseId, string BlobRef) : Synapse;

public sealed record ExpenseDriveUploadFailed(
    string ExpenseId,
    string Reason,
    string Scope) : Synapse;

public sealed record AuthorizationRequired(
    string CorrelationId,
    string Scope,
    string ExpenseId) : Synapse;

public sealed record ExpenseWorkflowPaused(string ExpenseId, string AtStep) : Synapse;

public sealed record AuthorizationGranted(string CorrelationId, string Scope) : Synapse;

public sealed record ExpenseDriveUploaded(string ExpenseId, string DriveFileId) : Synapse;

public sealed record ExpenseFiled(string ExpenseId, string FinanceId) : Synapse;

// Multi-step: Gmail ok → Drive 401 pause → grant completes drive → file expense without re-fetching Gmail.
public sealed class ExpenseWorkflow : Neuron<ExpenseWorkflowState>,
    INeuron<ExpenseWorkflowStart>,
    INeuron<ExpenseDriveUploadFailed>,
    INeuron<AuthorizationGranted>,
    INeuron<ExpenseDriveUploaded>
{
    public Task HandleAsync(ExpenseWorkflowStart fact, CancellationToken cancellationToken)
    {
        State.ExpenseId = fact.ExpenseId;
        State.ReceiptBlobRef = $"blob-{fact.ReceiptMessageId}";
        State.GmailDone = true;
        State.Paused = false;
        Emit(new ExpenseGmailFetched(fact.ExpenseId, State.ReceiptBlobRef));
        Emit(new ExpenseDriveUploadAsked(fact.ExpenseId, State.ReceiptBlobRef));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ExpenseDriveUploadFailed fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.ExpenseId)
            || !string.Equals(fact.Reason, "authorization-expired", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.Paused = true;
        State.AuthCorrelation = $"auth-{fact.ExpenseId}";
        Emit(new AuthorizationRequired(State.AuthCorrelation, fact.Scope, fact.ExpenseId));
        Emit(new ExpenseWorkflowPaused(fact.ExpenseId, AtStep: "drive"));
        return Task.CompletedTask;
    }

    public Task HandleAsync(AuthorizationGranted fact, CancellationToken cancellationToken)
    {
        if (State.AuthCorrelation is null
            || !string.Equals(State.AuthCorrelation, fact.CorrelationId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Journal resume intent only — Drive connector owns completing the pending upload.
        State.Paused = false;
        return Task.CompletedTask;
    }

    public Task HandleAsync(ExpenseDriveUploaded fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.ExpenseId) || State.Filed)
        {
            return Task.CompletedTask;
        }

        State.Filed = true;
        State.Paused = false;
        Emit(new ExpenseFiled(fact.ExpenseId, FinanceId: $"fin-{fact.ExpenseId}"));
        return Task.CompletedTask;
    }

    private bool Matches(string expenseId)
        => string.Equals(State.ExpenseId, expenseId, StringComparison.Ordinal);
}

public sealed class ExpenseWorkflowState
{
    public string? ExpenseId { get; set; }
    public string? ReceiptBlobRef { get; set; }
    public bool GmailDone { get; set; }
    public bool Paused { get; set; }
    public bool Filed { get; set; }
    public string? AuthCorrelation { get; set; }
}

// Drive: first upload fails auth and pins pending; AuthorizationGranted completes that pending upload.
public sealed class ExpenseDriveConnector : Neuron<ExpenseDriveConnectorState>,
    INeuron<ExpenseDriveUploadAsked>,
    INeuron<AuthorizationGranted>
{
    public Task HandleAsync(ExpenseDriveUploadAsked fact, CancellationToken cancellationToken)
    {
        if (!State.Authorized)
        {
            State.PendingExpenseId = fact.ExpenseId;
            State.PendingBlobRef = fact.BlobRef;
            Emit(new ExpenseDriveUploadFailed(
                fact.ExpenseId,
                Reason: "authorization-expired",
                Scope: "drive.file"));
            return Task.CompletedTask;
        }

        Emit(new ExpenseDriveUploaded(fact.ExpenseId, DriveFileId: $"drive-{fact.BlobRef}"));
        return Task.CompletedTask;
    }

    public Task HandleAsync(AuthorizationGranted fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.Scope, "drive.file", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.Authorized = true;
        if (State.PendingExpenseId is null || State.PendingBlobRef is null)
        {
            return Task.CompletedTask;
        }

        Emit(new ExpenseDriveUploaded(
            State.PendingExpenseId,
            DriveFileId: $"drive-{State.PendingBlobRef}"));
        State.PendingExpenseId = null;
        State.PendingBlobRef = null;
        return Task.CompletedTask;
    }
}

public sealed class ExpenseDriveConnectorState
{
    public bool Authorized { get; set; }
    public string? PendingExpenseId { get; set; }
    public string? PendingBlobRef { get; set; }
}

public sealed class ExpenseWorkflowLedger : Neuron,
    INeuron<ExpenseGmailFetched>,
    INeuron<AuthorizationRequired>,
    INeuron<ExpenseWorkflowPaused>,
    INeuron<AuthorizationGranted>,
    INeuron<ExpenseDriveUploaded>,
    INeuron<ExpenseFiled>,
    INeuron<ExpenseDriveUploadFailed>
{
    public Task HandleAsync(ExpenseGmailFetched fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(AuthorizationRequired fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ExpenseWorkflowPaused fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(AuthorizationGranted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ExpenseDriveUploaded fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ExpenseFiled fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ExpenseDriveUploadFailed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
