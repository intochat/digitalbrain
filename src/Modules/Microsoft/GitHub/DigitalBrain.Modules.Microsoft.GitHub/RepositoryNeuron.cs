using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;

[GrainType("github.repository")]
internal sealed class RepositoryNeuron(
    GitHubRepositoryBindings bindings,
    IGitHubRepositorySource source,
    GitHubSetupService setup,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<RepositoryState> state)
    : Neuron, IRepository
{
    private const int MaxProcessedDeliveries = 512;

    private GitHubRepositoryBinding Binding => bindings.GetFor(this.GetPrimaryKeyString());

    public async Task<RepositoryView> Connect(ConnectRepository request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.AppId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.InstallationId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.RepositoryId, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryName);

        var previous = state.State ?? new RepositoryState();
        var binding = Binding;
        if (!binding.Enabled)
        {
            await PublishAsync(new RepositoryRefused("The configured GitHub repository is revoked."));
            return View();
        }

        if (request.AppId != binding.AppId || request.InstallationId != binding.InstallationId
            || request.RepositoryId != binding.RepositoryId
            || !string.Equals(request.RepositoryOwner, binding.RepoOwner, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RepositoryName, binding.RepoName, StringComparison.OrdinalIgnoreCase))
        {
            await PublishAsync(new RepositoryRefused("The connection does not match its authorized binding."));
            return View();
        }

        var next = Transition(previous, webhookAt: null, deliveryId: null);
        if (next.Revoked)
        {
            await RevokeAsync(next);
            return View();
        }

        state.State = next;
        await state.WriteStateAsync();
        await PublishAsync(new RepositoryConnected(binding.Id, binding.RepositoryId, binding.InstallationId));
        return View();
    }

    public async Task<RepositoryView> Refresh(RefreshRepository request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var previous = state.State ?? new RepositoryState();
        if (previous.Revoked)
        {
            throw new GitHubAccessDeniedException("Reconnect repository access before refreshing.");
        }

        if (request.Number is { } number)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        }

        await ApplyAsync(previous, request.Number, trigger: null, webhookAt: null, deliveryId: null, CancellationToken.None);
        return View();
    }

    public async Task<bool> AcceptRepositoryEvent(RepositoryEvent receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(receipt.DeliveryId);
        var previous = state.State ?? new RepositoryState();
        if ((previous.ProcessedDeliveries ?? []).Contains(receipt.DeliveryId, StringComparer.Ordinal))
        {
            return false;
        }

        await ApplyAsync(previous, receipt.Number, receipt, TimeProvider.System.GetUtcNow(), receipt.DeliveryId, cancellationToken);
        return true;
    }

    [ReadOnly]
    public Task<RepositoryView> Read() => Task.FromResult(View());

    [ReadOnly]
    public Task<PullRequestsRead> ReadPullRequests()
        => Task.FromResult(new PullRequestsRead(View().PullRequests, state.State is { Revoked: false, BindingRevision: not null }));

    [ReadOnly]
    public Task<GitHubSetupResult> ResolveRepository(ResolveRepository query, CancellationToken cancellationToken = default)
        => setup.ResolveAsync(query.RepositoryUrl, cancellationToken, View());

    [ReadOnly]
    public async Task<PullRequestRead> ReadPullRequest(ReadPullRequest query, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return new(await source.GetPullRequestAsync(Binding, query.Number, cancellationToken).ConfigureAwait(true));
    }

    [ReadOnly]
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

    [ReadOnly]
    public Task<RequiredChecksRead> ReadRequiredChecks(CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return source.GetRequiredChecksAsync(Binding, null, cancellationToken);
    }

    private async Task ApplyAsync(RepositoryState previous, int? number, RepositoryEvent? trigger,
        DateTimeOffset? webhookAt, string? deliveryId, CancellationToken cancellationToken)
    {
        var next = Transition(previous, webhookAt, deliveryId);
        if (trigger?.Action == "revoked" || !Binding.Enabled || next.Revoked)
        {
            await RevokeAsync(next);
            return;
        }

        var facts = new List<PullRequestChanged>();
        if (trigger?.Event != "ping")
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            foreach (var snapshot in await RefreshAsync(number, previous, deadline.Token))
            {
                if (Apply(next, snapshot, facts, trigger) is { } reason)
                {
                    state.State = previous with { ProcessedDeliveries = next.ProcessedDeliveries };
                    await state.WriteStateAsync();
                    await PublishAsync(new RepositoryRefused(reason));
                    return;
                }
            }
        }

        state.State = next;
        await state.WriteStateAsync();
        foreach (var fact in facts)
        {
            await PublishAsync(fact);
        }
    }

    private RepositoryState Transition(RepositoryState previous, DateTimeOffset? webhookAt, string? deliveryId)
    {
        var sameRevision = previous.BindingRevision == Binding.Revision;
        var deliveries = new List<string>(previous.ProcessedDeliveries ?? []);
        if (deliveryId is not null)
        {
            deliveries.Add(deliveryId);
            if (deliveries.Count > MaxProcessedDeliveries)
            {
                deliveries.RemoveRange(0, deliveries.Count - MaxProcessedDeliveries);
            }
        }

        return previous with
        {
            PullRequests = new(previous.PullRequests ?? []),
            ProcessedDeliveries = deliveries,
            BindingRevision = Binding.Revision,
            Revoked = sameRevision && previous.Revoked,
            LastWebhookAt = webhookAt ?? (sameRevision ? previous.LastWebhookAt : null),
        };
    }

    private async Task RevokeAsync(RepositoryState next)
    {
        Binding.Revoke();
        state.State = next with { Revoked = true };
        await state.WriteStateAsync();
        await PublishAsync(new RepositoryAccessRevoked(Binding.Id));
    }

    private async Task<IReadOnlyList<PullRequestSnapshot>> RefreshAsync(int? number, RepositoryState previous, CancellationToken token)
    {
        if (number is { } selected)
        {
            return [await source.GetPullRequestAsync(Binding, selected, token).ConfigureAwait(true)];
        }

        var open = await source.ListOpenPullRequestsAsync(Binding, token).ConfigureAwait(true);
        var all = open.ToList();
        foreach (var old in (previous.PullRequests ?? []).Values.Where(item => item.IsOpen && !open.Any(current => current.Number == item.Number)))
        {
            all.Add(await source.GetPullRequestAsync(Binding, old.Number, token).ConfigureAwait(true));
        }

        return all;
    }

    private string? Apply(RepositoryState state, PullRequestSnapshot snapshot, List<PullRequestChanged> facts, RepositoryEvent? trigger)
    {
        if (snapshot.RepositoryId != Binding.RepositoryId || snapshot.Number <= 0)
        {
            return "Observed evidence belongs to a different repository.";
        }

        var previous = state.PullRequests.GetValueOrDefault(snapshot.Number);
        if (previous is null && state.PullRequests.Count >= 512)
        {
            var expired = state.PullRequests.Values.Where(item => !item.IsOpen).OrderBy(item => item.ObservedAt).FirstOrDefault();
            if (expired is null)
            {
                return "The repository observation capacity is full.";
            }

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

        if (trigger?.Number == snapshot.Number && trigger.Action is "opened" or "reopened" or "closed")
        {
            change |= trigger.Action == "closed" ? PullRequestChange.Closed : PullRequestChange.Opened;
        }

        if (change != 0)
        {
            facts.Add(new(snapshot.Number, $"{snapshot.Revision}:{snapshot.CiRevision}", $"github:{snapshot.RepositoryId}:{snapshot.Number}"));
        }

        return null;
    }

    private void RequireAvailable()
    {
        if (state.State?.Revoked == true && state.State.BindingRevision == Binding.Revision)
        {
            throw new GitHubAccessDeniedException("Reconnect repository access before reading evidence.");
        }

        Binding.RequireEnabled();
    }

    private RepositoryView View() => new(this.GetPrimaryKeyString(), state.State is { BindingRevision: not null, Revoked: false },
        state.State?.Revoked ?? false, (state.State?.PullRequests ?? []).Values.OrderBy(item => item.Number).ToArray() ?? [], state.State?.LastWebhookAt);
}