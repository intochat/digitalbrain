namespace DigitalBrain.Core.Tests.Scenarios;

// Work facts live on the thread neuron — devices only contribute; they are not forked work identities.
public sealed record WorkDraftCommitted(string Device, string DraftText, string ThreadKey) : Synapse;

public sealed record WorkProgressLogged(string Device, string Note, string ThreadKey) : Synapse;

public sealed record DevicePresenceChanged(string Device, string Status, string ThreadKey) : Synapse;

public sealed class WorkThreadState
{
    public string? DraftText { get; set; }
    public string? LastDevice { get; set; }
    public string? ThreadKey { get; set; }
}

// Durable chat/thread stand-in: all work journals here; phone and desktop only Send into it.
public sealed class WorkThread : Neuron<WorkThreadState>,
    INeuron<WorkDraftCommitted>,
    INeuron<WorkProgressLogged>,
    INeuron<DevicePresenceChanged>
{
    public Task HandleAsync(WorkDraftCommitted fact, CancellationToken cancellationToken)
    {
        State.DraftText = fact.DraftText;
        State.LastDevice = fact.Device;
        State.ThreadKey = fact.ThreadKey;
        return Task.CompletedTask;
    }

    public Task HandleAsync(WorkProgressLogged fact, CancellationToken cancellationToken)
    {
        State.LastDevice = fact.Device;
        State.ThreadKey = fact.ThreadKey;
        return Task.CompletedTask;
    }

    public Task HandleAsync(DevicePresenceChanged fact, CancellationToken cancellationToken)
    {
        State.LastDevice = fact.Device;
        State.ThreadKey = fact.ThreadKey;
        return Task.CompletedTask;
    }
}
