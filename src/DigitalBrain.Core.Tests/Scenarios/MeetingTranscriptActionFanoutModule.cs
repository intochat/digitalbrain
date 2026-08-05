namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MeetingTranscriptReady(
    string MeetingId,
    string TranscriptText,
    IReadOnlyList<string> Attendees) : Synapse;

public sealed record ActionItemCreated(
    string ActionId,
    string MeetingId,
    string Text,
    string Lane) : Synapse;

public sealed record ActionLaneAcknowledged(
    string ActionId,
    string Lane,
    string MeetingId) : Synapse;

// Transcript → multiple ActionItemCreated (one per lane) as ambient fan-out.
public sealed class MeetingActionExtractor : Neuron, INeuron<MeetingTranscriptReady>
{
    public const string LaneTasks = "tasks";
    public const string LaneEmail = "email";
    public const string LaneCrm = "crm";

    public Task HandleAsync(MeetingTranscriptReady fact, CancellationToken cancellationToken)
    {
        Emit(new ActionItemCreated(
            ActionId: $"act-tasks-{fact.MeetingId}",
            MeetingId: fact.MeetingId,
            Text: "Create follow-up tasks from decisions",
            Lane: LaneTasks));
        Emit(new ActionItemCreated(
            ActionId: $"act-email-{fact.MeetingId}",
            MeetingId: fact.MeetingId,
            Text: "Draft follow-up email to attendees",
            Lane: LaneEmail));
        Emit(new ActionItemCreated(
            ActionId: $"act-crm-{fact.MeetingId}",
            MeetingId: fact.MeetingId,
            Text: "Log activity in CRM",
            Lane: LaneCrm));
        return Task.CompletedTask;
    }
}

// Distinct handlers at same locus: each filters its lane and acknowledges (declared fan-out proof).
public sealed class ActionTasksHandler : Neuron, INeuron<ActionItemCreated>
{
    public Task HandleAsync(ActionItemCreated fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.Lane, MeetingActionExtractor.LaneTasks, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(new ActionLaneAcknowledged(fact.ActionId, fact.Lane, fact.MeetingId));
        return Task.CompletedTask;
    }
}

public sealed class ActionEmailHandler : Neuron, INeuron<ActionItemCreated>
{
    public Task HandleAsync(ActionItemCreated fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.Lane, MeetingActionExtractor.LaneEmail, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(new ActionLaneAcknowledged(fact.ActionId, fact.Lane, fact.MeetingId));
        return Task.CompletedTask;
    }
}

public sealed class ActionCrmHandler : Neuron, INeuron<ActionItemCreated>
{
    public Task HandleAsync(ActionItemCreated fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.Lane, MeetingActionExtractor.LaneCrm, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(new ActionLaneAcknowledged(fact.ActionId, fact.Lane, fact.MeetingId));
        return Task.CompletedTask;
    }
}

// Catalog sink for ActionLaneAcknowledged ambient emits.
public sealed class ActionLaneLedger : Neuron, INeuron<ActionLaneAcknowledged>
{
    public Task HandleAsync(ActionLaneAcknowledged fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
