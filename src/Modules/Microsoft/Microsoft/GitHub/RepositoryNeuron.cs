using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;

[GrainType("repository")]
internal sealed class RepositoryNeuron(
    NeuronRuntime runtime,
    GitHubRepositoryBindings bindings,
    IGitHubRepositorySource source,
    GitHubSetupService setup,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<RepositoryState>> state)
    : Neuron<RepositoryState>(runtime, state), IRepository
{
    private GitHubRepositoryBinding Binding => bindings.GetFor(Id);

    public Task<Accepted<RepositoryView>> Connect(ConnectRepository command) => ExecuteCommandAsync(
        Descriptor("connect"), command, GitHubJson.Default.ConnectRepository, GitHubJson.Default.AcceptedRepositoryView, arguments =>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(arguments.AppId, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(arguments.InstallationId, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(arguments.RepositoryId, 1);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.RepositoryOwner);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.RepositoryName);
            var work = Schedule(Signal.FromJson(GitHubSignals.RepositoryConnected, arguments, GitHubJson.Default.ConnectRepository));
            return new Accepted<RepositoryView>(View(), work);
        });

    public Task<Accepted<RepositoryView>> Refresh(RefreshRepository command) => ExecuteCommandAsync(
        Descriptor("refresh"), command, GitHubJson.Default.RefreshRepository, GitHubJson.Default.AcceptedRepositoryView, arguments =>
        {
            // Fast-fail only; the reaction re-validates revocation.
            if (State?.Revoked == true)
            {
                throw new GitHubAccessDeniedException("Reconnect repository access before refreshing.");
            }
            if (arguments.Number is { } number)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
            }
            var work = Schedule(Signal.FromJson(GitHubSignals.RepositoryRefreshRequested, arguments, GitHubJson.Default.RefreshRepository));
            return new Accepted<RepositoryView>(View(), work);
        });

    public Task<RepositoryView> Read() => Task.FromResult(View());
    public Task<PullRequestsRead> ReadPullRequests() => Task.FromResult(new PullRequestsRead(View().PullRequests, State is { Revoked: false, BindingRevision: not null }));
    public Task<GitHubSetupResult> ResolveRepository(ResolveRepository query, CancellationToken cancellationToken = default)
        => setup.ResolveAsync(query.RepositoryUrl, cancellationToken, View());

    public async Task<PullRequestRead> ReadPullRequest(ReadPullRequest query, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return new(await source.GetPullRequestAsync(Binding, query.Number, cancellationToken).ConfigureAwait(true));
    }

    public async Task<ReviewEvidenceRead> ReadReviewEvidence(ReadReviewEvidence query, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        var current = await source.GetPullRequestAsync(Binding, query.Expected.Number, cancellationToken).ConfigureAwait(true);
        if (current.Revision != query.Expected.Revision || current.CiRevision != query.Expected.CiRevision)
        {
            return new(current, null, "The PR or CI changed; use the current revision.");
        }
        var data = await source.GetReviewEvidenceAsync(Binding, current, cancellationToken).ConfigureAwait(true);
        var confirmation = await source.GetPullRequestAsync(Binding, current.Number, cancellationToken).ConfigureAwait(true);
        return confirmation.Revision == current.Revision && confirmation.CiRevision == current.CiRevision && data.Complete
            ? new(confirmation, data)
            : new(confirmation, null, "The evidence changed during retrieval or was incomplete.");
    }

    public Task<RequiredChecksRead> ReadRequiredChecks(CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return source.GetRequiredChecksAsync(Binding, null, cancellationToken);
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        RepositoryEvent? trigger = null;
        int? number;
        switch (delivery.Signal.Type)
        {
            case GitHubSignals.RepositoryConnected:
                if (Body(delivery, GitHubJson.Default.ConnectRepository) is not { } command)
                {
                    return;
                }

                var binding = Binding;
                binding.RequireEnabled();
                if (command.AppId != binding.AppId || command.InstallationId != binding.InstallationId
                    || command.RepositoryId != binding.RepositoryId
                    || !string.Equals(command.RepositoryOwner, binding.RepoOwner, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(command.RepositoryName, binding.RepoName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new GitHubAccessDeniedException("The connection does not match its authorized binding.");
                }
                number = null;
                break;
            case GitHubSignals.RepositoryRefreshRequested:
                if (Body(delivery, GitHubJson.Default.RefreshRepository) is not { } refresh)
                {
                    return;
                }

                number = refresh.Number;
                break;
            case GitHubSignals.RepositoryEvent:
                if (Body(delivery, GitHubJson.Default.RepositoryEvent) is not { } repositoryEvent)
                {
                    return;
                }

                trigger = repositoryEvent;
                number = trigger.Number;
                break;
            default:
                return;
        }
        var previous = State ?? new RepositoryState();
        var next = previous with
        {
            PullRequests = new(previous.PullRequests),
            BindingRevision = Binding.Revision,
            Revoked = previous.BindingRevision == Binding.Revision && previous.Revoked,
            LastWebhookAt = trigger is not null ? delivery.Timestamp
                : previous.BindingRevision == Binding.Revision ? previous.LastWebhookAt : null,
        };
        if (trigger?.Action == "revoked" || !Binding.Enabled || next.Revoked)
        {
            await SaveAsync(next with { Revoked = true }, cancellationToken).ConfigureAwait(true);
            Binding.Revoke();
            await FireAsync(Signal.FromJson(GitHubSignals.RepositoryAccessRevoked,
                new RepositoryAccessRevoked(Binding.Id), GitHubJson.Default.RepositoryAccessRevoked), cancellationToken: cancellationToken).ConfigureAwait(true);
            return;
        }
        var facts = new List<PullRequestChanged>();
        if (trigger?.Event != "ping")
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            foreach (var snapshot in await RefreshAsync(number, previous, deadline.Token).ConfigureAwait(true))
            {
                Apply(next, snapshot, facts, trigger);
            }
        }
        await SaveAsync(next, cancellationToken).ConfigureAwait(true);
        foreach (var fact in facts)
        {
            await FireAsync(Signal.FromJson(GitHubSignals.PullRequestChanged, fact, GitHubJson.Default.PullRequestChanged),
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<IReadOnlyList<PullRequestSnapshot>> RefreshAsync(int? number, RepositoryState previous, CancellationToken token)
    {
        if (number is { } selected)
        {
            return [await source.GetPullRequestAsync(Binding, selected, token).ConfigureAwait(true)];
        }
        var open = await source.ListOpenPullRequestsAsync(Binding, token).ConfigureAwait(true);
        var all = open.ToList();
        foreach (var old in previous.PullRequests.Values.Where(item => item.IsOpen && !open.Any(current => current.Number == item.Number)))
        {
            all.Add(await source.GetPullRequestAsync(Binding, old.Number, token).ConfigureAwait(true));
        }
        return all;
    }

    private void Apply(RepositoryState state, PullRequestSnapshot snapshot, List<PullRequestChanged> facts, RepositoryEvent? trigger)
    {
        if (snapshot.RepositoryId != Binding.RepositoryId || snapshot.Number <= 0)
        {
            throw new GitHubAccessDeniedException("Observed evidence belongs to a different repository.");
        }
        var previous = state.PullRequests.GetValueOrDefault(snapshot.Number);
        if (previous is null && state.PullRequests.Count >= 512)
        {
            var expired = state.PullRequests.Values.Where(item => !item.IsOpen).OrderBy(item => item.ObservedAt).FirstOrDefault()
                ?? throw new InvalidOperationException("The repository observation capacity is full.");
            state.PullRequests.Remove(expired.Number);
        }
        state.PullRequests[snapshot.Number] = snapshot;
        var change = (PullRequestChange)0;
        if (previous is null || !previous.IsOpen && snapshot.IsOpen)
        {
            change |= snapshot.IsOpen ? PullRequestChange.Opened : PullRequestChange.Closed;
        }
        else if (previous.IsOpen && !snapshot.IsOpen)
        {
            change |= PullRequestChange.Closed;
        }
        else if (previous.Revision != snapshot.Revision || previous.IsDraft != snapshot.IsDraft)
        {
            change |= PullRequestChange.Updated;
        }
        if (previous is not null && previous.CiRevision != snapshot.CiRevision)
        {
            change |= PullRequestChange.Checks;
        }
        // Preserve lifecycle edges even when the authoritative read already sees a later state.
        if (trigger?.Number == snapshot.Number && trigger.Action is "opened" or "reopened" or "closed")
        {
            change |= trigger.Action == "closed" ? PullRequestChange.Closed : PullRequestChange.Opened;
        }
        if (change != 0)
        {
            facts.Add(new(snapshot.Number, $"{snapshot.Revision}:{snapshot.CiRevision}", $"github:{snapshot.RepositoryId}:{snapshot.Number}"));
        }
    }

    private void RequireAvailable()
    {
        if (State?.Revoked == true && State.BindingRevision == Binding.Revision)
        {
            throw new GitHubAccessDeniedException("Reconnect repository access before reading evidence.");
        }
        Binding.RequireEnabled();
    }

    private RepositoryView View() => new(Id.Name, State is { BindingRevision: not null, Revoked: false },
        State?.Revoked ?? false, State?.PullRequests.Values.OrderBy(item => item.Number).ToArray() ?? [], State?.LastWebhookAt);
}
