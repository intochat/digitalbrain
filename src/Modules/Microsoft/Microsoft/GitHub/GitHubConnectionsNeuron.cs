using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;

[GrainType("github")]
internal sealed class GitHubConnectionsNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<GitHubConnectionsState>> state)
    : Neuron<GitHubConnectionsState>(runtime, state), IGitHubConnections
{
    public Task<Accepted<GitHubConnectionRecord>> Register(RegisterGitHubConnection command) => ExecuteCommandAsync(
        Descriptor("register"), command, GitHubJson.Default.RegisterGitHubConnection, GitHubJson.Default.AcceptedGitHubConnectionRecord, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.ConnectionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.RepositoryOwner);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.RepositoryName);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Epoch);
            ArgumentOutOfRangeException.ThrowIfLessThan(arguments.AppId, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(arguments.InstallationId, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(arguments.RepositoryId, 1);
            // Fast-fail only; the reaction re-validates connection capacity.
            if (State?.Connections.Count(item => item.Id != arguments.ConnectionId) >= 256)
            {
                throw new InvalidOperationException("The GitHub connection capacity is full.");
            }
            var record = new GitHubConnectionRecord(arguments.ConnectionId, arguments.AppId, arguments.InstallationId,
                arguments.RepositoryId, arguments.RepositoryOwner, arguments.RepositoryName, arguments.Epoch);
            var work = Schedule(Signal.FromJson(GitHubSignals.GitHubConnectionRegistered,
                record, GitHubJson.Default.GitHubConnectionRecord));
            return new Accepted<GitHubConnectionRecord>(record, work);
        });

    public Task<GitHubConnectionList> List() => Task.FromResult(new GitHubConnectionList(State?.Connections ?? []));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != GitHubSignals.GitHubConnectionRegistered)
        {
            return;
        }
        if (Body(delivery, GitHubJson.Default.GitHubConnectionRecord) is not { } record)
        {
            return;
        }

        var items = State?.Connections.Where(item => item.Id != record.Id).ToList() ?? [];
        if (items.Count >= 256)
        {
            throw new InvalidOperationException("The GitHub connection capacity is full.");
        }
        items.Add(record);
        await SaveAsync(new GitHubConnectionsState(items), cancellationToken).ConfigureAwait(true);
    }
}
