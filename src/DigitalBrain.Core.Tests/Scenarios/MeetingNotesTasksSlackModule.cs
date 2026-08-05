namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MeetingNotesReady(
    string MeetingId,
    string NotesText,
    string ChannelHint) : Synapse;

public sealed record NotesTaskCreated(
    string TaskId,
    string Title,
    string MeetingId) : Synapse;

public sealed record SlackPostRequested(
    string MeetingId,
    string Channel,
    string Text) : Synapse;

public sealed record SlackMessagePosted(
    string MeetingId,
    string Channel,
    string Permalink) : Synapse;

// Notes ingress: fan-out durable task candidates + collab post request (mock Slack, no network).
public sealed class MeetingNotesExtractor : Neuron, INeuron<MeetingNotesReady>
{
    public Task HandleAsync(MeetingNotesReady fact, CancellationToken cancellationToken)
    {
        Emit(new NotesTaskCreated(
            TaskId: $"notes-task-1-{fact.MeetingId}",
            Title: "Send proposal to Acme",
            MeetingId: fact.MeetingId));
        Emit(new NotesTaskCreated(
            TaskId: $"notes-task-2-{fact.MeetingId}",
            Title: "Schedule technical deep-dive",
            MeetingId: fact.MeetingId));
        Emit(new SlackPostRequested(
            fact.MeetingId,
            Channel: fact.ChannelHint,
            Text: $"Meeting {fact.MeetingId}: 2 tasks created from notes."));
        return Task.CompletedTask;
    }
}

// Catalog sink for NotesTaskCreated ambient fan-out.
public sealed class NotesTaskStore : Neuron, INeuron<NotesTaskCreated>
{
    public Task HandleAsync(NotesTaskCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Mock Slack adapter: request → posted fact.
public sealed class MockSlackPoster : Neuron, INeuron<SlackPostRequested>
{
    public Task HandleAsync(SlackPostRequested fact, CancellationToken cancellationToken)
    {
        Emit(new SlackMessagePosted(
            fact.MeetingId,
            fact.Channel,
            Permalink: $"https://slack.test/{fact.Channel}/{fact.MeetingId}"));
        return Task.CompletedTask;
    }
}

// Catalog sink for SlackMessagePosted ambient emit.
public sealed class SlackPostLedger : Neuron, INeuron<SlackMessagePosted>
{
    public Task HandleAsync(SlackMessagePosted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
