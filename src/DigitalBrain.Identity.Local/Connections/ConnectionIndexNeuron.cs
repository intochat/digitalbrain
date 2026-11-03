using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Rules;

namespace DigitalBrain.Identity.Local.Connections;

/// <summary>Persisted state of <see cref="ConnectionIndexNeuron"/>.</summary>
public sealed class ConnectionIndexState
{
    /// <summary>The tracked connection keys in the brain.</summary>
    public string[] Keys { get; set; } = [];
}

/// <summary>Plain neuron tracking the connection keys of one brain.</summary>
[Neuron(IConnectionIndex.TypeName)]
public sealed class ConnectionIndexNeuron : Neuron<ConnectionIndexState>, IConnectionIndex
{
    /// <inheritdoc />
    public Task<string[]> List() => Task.FromResult<string[]>([.. State.Keys]);

    /// <inheritdoc />
    public async Task Track(string connectionKey)
    {
        await Require("Track", connectionKey).ConfigureAwait(true);
        if (State.Keys.Contains(connectionKey, StringComparer.Ordinal))
        {
            return;
        }

        await Persist(new ConnectionIndexState { Keys = [.. State.Keys, connectionKey] }).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task Untrack(string connectionKey)
    {
        await Require("Untrack", connectionKey).ConfigureAwait(true);
        if (!State.Keys.Contains(connectionKey, StringComparer.Ordinal))
        {
            return;
        }

        await Persist(new ConnectionIndexState { Keys = [.. State.Keys.Where(key => key != connectionKey)] }).ConfigureAwait(true);
    }

    private async Task Persist(ConnectionIndexState next)
    {
        await Context.State.Save(next, []).ConfigureAwait(true);
        State.Keys = next.Keys;
    }

    private Task Require(string operation, string connectionKey)
        => Check(operation, new ConnectionBelongsToBrain(ConnectionKeys.Parse(connectionKey).BrainId, Id.Brain.Value));
}

internal sealed record ConnectionBelongsToBrain(string ConnectionBrainId, string ExpectedBrainId) : Rule("identity.connection-belongs-to-brain")
{
    public override bool Broken => ConnectionBrainId != ExpectedBrainId;
    public override string Message => "The connection belongs to another brain.";
}

internal sealed record IntegrationIsAvailable(string Kind, bool Available) : Rule("identity.integration-available")
{
    public override bool Broken => !Available;
    public override string Message => $"Integration '{Kind}' is not available.";
}
