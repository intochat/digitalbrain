namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record CallEnded(string CallId, string RecordingRef, string ContactHint) : Synapse;

public sealed record CallTranscriptReady(string CallId, string Text) : Synapse;

public sealed record CallSummarized(string CallId, string Summary) : Synapse;

public sealed record CallActionItemsProposed(string CallId, string ItemsCsv) : Synapse;

public sealed record ResolveContactAsked(string CallId, string ContactHint) : Synapse;

public sealed record ContactResolved(string CallId, string ContactId, string Email) : Synapse;

public sealed record CrmNoteLogged(string CallId, string ContactId, string Note) : Synapse;

public sealed record FollowUpEmailDrafted(string CallId, string To, string Subject, string Body) : Synapse;

// Transcriber mock: CallEnded → CallTranscriptReady.
public sealed class CallTranscriber : Neuron, INeuron<CallEnded>
{
    public Task HandleAsync(CallEnded fact, CancellationToken cancellationToken)
    {
        Emit(new CallTranscriptReady(
            fact.CallId,
            Text: $"Discussed renewal with {fact.ContactHint}. Follow up with pricing sheet by Friday. [hint:{fact.ContactHint}]"));
        return Task.CompletedTask;
    }
}

// Coach: transcript → summary + actions + contact resolve ask → CRM note + email draft on resolve.
public sealed class CallCoach : Neuron<CallCoachState>,
    INeuron<CallTranscriptReady>,
    INeuron<ContactResolved>
{
    public Task HandleAsync(CallTranscriptReady fact, CancellationToken cancellationToken)
    {
        State.CallId = fact.CallId;
        State.Transcript = fact.Text;
        Emit(new CallSummarized(fact.CallId, Summary: "Renewal discussion; send pricing by Friday."));
        Emit(new CallActionItemsProposed(fact.CallId, ItemsCsv: "send-pricing"));
        var hint = fact.Text.Contains("Acme", StringComparison.OrdinalIgnoreCase) ? "Acme CFO" : "contact";
        Ask<ContactResolved>(new ResolveContactAsked(fact.CallId, hint));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ContactResolved fact, CancellationToken cancellationToken)
    {
        if (State.CallId is null || !string.Equals(State.CallId, fact.CallId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(new CrmNoteLogged(
            fact.CallId,
            fact.ContactId,
            Note: State.Transcript ?? "call"));
        Emit(new FollowUpEmailDrafted(
            fact.CallId,
            To: fact.Email,
            Subject: $"Follow-up on {fact.CallId}",
            Body: "Thanks for the call — pricing sheet attached."));
        return Task.CompletedTask;
    }
}

public sealed class CallCoachState
{
    public string? CallId { get; set; }
    public string? Transcript { get; set; }
}

public sealed class CallContactDirectory : Neuron, IAnswers<ResolveContactAsked, ContactResolved>
{
    public Task<ContactResolved?> HandleAsync(
        ResolveContactAsked question, CancellationToken cancellationToken)
        => Task.FromResult<ContactResolved?>(new ContactResolved(
            question.CallId,
            ContactId: "crm-acme-1",
            Email: "cfo@acme.example"));
}

public sealed class CallWorkflowLedger : Neuron,
    INeuron<CallTranscriptReady>,
    INeuron<CallSummarized>,
    INeuron<CallActionItemsProposed>,
    INeuron<CrmNoteLogged>,
    INeuron<FollowUpEmailDrafted>
{
    public Task HandleAsync(CallTranscriptReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CallSummarized fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CallActionItemsProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CrmNoteLogged fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(FollowUpEmailDrafted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
