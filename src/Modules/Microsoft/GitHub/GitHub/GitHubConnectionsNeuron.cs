using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;

[GrainType("github.connections")]
internal sealed class GitHubConnectionsNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GitHubConnectionsState> state)
    : Neuron, IGitHubConnections
{
    private const int MaxConnections = 256;

    public async Task<GitHubConnectionRecord> Register(RegisterGitHubConnection request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Epoch);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.AppId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.InstallationId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.RepositoryId, 1);

        var record = new GitHubConnectionRecord(request.ConnectionId, request.AppId, request.InstallationId,
            request.RepositoryId, request.RepositoryOwner, request.RepositoryName, request.Epoch);
        var connections = (state.State?.Connections ?? []).Where(item => item.Id != record.Id).ToList();
        if (connections.Count >= MaxConnections)
        {
            await PublishAsync(new RepositoryRefused("The GitHub connection capacity is full."));
            return record;
        }

        connections.Add(record);
        state.State = new GitHubConnectionsState(connections);
        await state.WriteStateAsync();
        await PublishAsync(new GitHubConnectionRegistered(record));
        return record;
    }

    [ReadOnly]
    public Task<GitHubConnectionList> List()
        => Task.FromResult(new GitHubConnectionList(state.State?.Connections ?? []));
}