namespace DigitalBrain.Core.Tests.Scenarios;

// Owner-only raw material — must never appear on the guest neuron journal.
public sealed record OwnerPrivateNote(string SecretSnippet, string HeadlineMetric, string PaneId) : Synapse;

public sealed record SharePaneRequested(string PaneId, string GuestId, string Recipient) : Synapse;

// Redacted projection: headline only — no secret snippet, no raw journal replay.
public sealed record SharedProjection(
    string PaneId,
    string GuestId,
    string HeadlineMetric,
    string Recipient) : Synapse;

public sealed class ShareGatewayState
{
    public string? SecretSnippet { get; set; }
    public string? HeadlineMetric { get; set; }
    public string? PaneId { get; set; }
}

// Owner share gateway: builds SharedProjection from allowlisted fields only.
public sealed class ShareGateway : Neuron<ShareGatewayState>,
    INeuron<OwnerPrivateNote>,
    INeuron<SharePaneRequested>
{
    public Task HandleAsync(OwnerPrivateNote fact, CancellationToken cancellationToken)
    {
        State.SecretSnippet = fact.SecretSnippet;
        State.HeadlineMetric = fact.HeadlineMetric;
        State.PaneId = fact.PaneId;
        return Task.CompletedTask;
    }

    public Task HandleAsync(SharePaneRequested fact, CancellationToken cancellationToken)
    {
        if (State.HeadlineMetric is null
            || State.PaneId is null
            || !string.Equals(State.PaneId, fact.PaneId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharePaneRequested requires a prior journaled OwnerPrivateNote for the same pane.");
        }

        // Redaction: secret never leaves on SharedProjection.
        Emit(new SharedProjection(
            fact.PaneId,
            fact.GuestId,
            State.HeadlineMetric,
            fact.Recipient));
        return Task.CompletedTask;
    }
}

// Guest view: only declares SharedProjection — cannot hear OwnerPrivateNote by catalog.
public sealed class GuestPaneViewer : Neuron, INeuron<SharedProjection>
{
    public Task HandleAsync(SharedProjection fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Owner-side audit sink for private notes (proves they journal on owner path only).
public sealed class OwnerPrivateAudit : Neuron, INeuron<OwnerPrivateNote>
{
    public Task HandleAsync(OwnerPrivateNote fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
