using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.AI;
using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.GitHub;

[GrainType("github-review-worker")]
internal sealed class PullRequestReviewWorker(NeuronRuntime runtime,
    GitHubRepositoryBindings bindings, IGitHubRepositorySource source) : Neuron(runtime), IPullRequestReviewWorker
{
    public async Task<PullRequestSnapshot[]> SynchronizeAsync(NeuronId inbox, string bindingId, ActorContext principal,
        bool enabled, CancellationToken cancellationToken)
    {
        if (VerifiedActor.Current?.PrincipalId != principal.PrincipalId || inbox.Type != "pullrequestreview"
            || !PrincipalPartition.TryParse(inbox.Name, out var inboxPrincipal, out var inboxLocal)
            || inboxPrincipal != principal.PrincipalId
            || Id.Name != PrincipalPartition.InstanceName(principal.PrincipalId, "sync-" + inboxLocal))
        {
            throw new NeuronAuthorizationException("The review subscription worker does not match its authenticated caller.");
        }
        using var actor = VerifiedActor.Enter(principal);
        var binding = bindings.Find(bindingId) ?? throw new InvalidOperationException("The repository binding is unavailable.");
        if (binding.Owner != Id.Owner || binding.Principal != principal.PrincipalId || inbox.Owner != Id.Owner
            || !PrincipalPartition.OwnsInstance(principal.PrincipalId, inbox.Name))
        {
            throw new NeuronAuthorizationException("The review subscription belongs to another principal.");
        }
        if (!binding.RecoveryComplete) { return []; }
        var repository = NeuronId.For<IRepository>(binding.Owner, binding.InstanceName);
        var sourceGrain = GrainFactory.GetGrain<INeuronGrain>(repository.ToGrainId());
        foreach (var type in new[] { nameof(PullRequestOpened), nameof(PullRequestUpdated), nameof(PullRequestClosed), nameof(PullRequestChecksChanged), nameof(RepositoryAccessRevoked) })
        {
            // The worker waits on the source while the inbox remains free to acknowledge
            // broadcasts. All edges still belong to the source's kernel binding boundary.
            await (enabled && binding.Enabled ? sourceGrain.BindOutgoing(inbox, type) : sourceGrain.UnbindOutgoing(inbox, type))
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken).ConfigureAwait(true);
        }
        if (!enabled || !binding.Enabled) { return []; }
        var snapshot = await RequestAsync<PullRequestsRead>(repository, new ReadPullRequests(), cancellationToken).ConfigureAwait(true);
        return snapshot.Available ? snapshot.Snapshots : [];
    }

    public async Task RunAsync(ReviewWork work, CancellationToken cancellationToken)
    {
        using var activity = GitHubTelemetry.Source.StartActivity("github.pull_request.review");
        activity?.SetTag("github.review.run_id", work.Run.Id.ToString("D"))
            .SetTag("github.review.generation", work.Run.Generation)
            .SetTag("github.repository.id", work.Run.Snapshot.RepositoryId)
            .SetTag("github.pull_request.number", work.Run.Snapshot.Number)
            .SetTag("github.pull_request.head_sha", work.Run.Snapshot.HeadSha)
            .SetTag("github.pull_request.base_sha", work.Run.Snapshot.BaseSha)
            .SetTag("github.pull_request.ci_sha", work.Run.Snapshot.CiSha);
        if (VerifiedActor.Current?.PrincipalId != work.Actor.PrincipalId || work.Inbox.Type != "pullrequestreview"
            || Id.Name != PrincipalPartition.InstanceName(work.Actor.PrincipalId, $"review-{work.Run.Id:N}-{work.Run.Generation}"))
        {
            throw new NeuronAuthorizationException("The review worker does not match its authenticated caller and generation.");
        }
        using var actor = VerifiedActor.Enter(work.Actor);
        var binding = bindings.Get(work.BindingId, work.Actor.PrincipalId, Id.Owner);
        if (work.Inbox.Owner != Id.Owner || !PrincipalPartition.OwnsInstance(work.Actor.PrincipalId, work.Inbox.Name)
            || binding.Revision != work.Run.BindingRevision)
        {
            throw new InvalidOperationException("The review binding changed.");
        }
        var snapshot = await source.GetPullRequestAsync(binding, work.Run.Snapshot.Number, cancellationToken).ConfigureAwait(true);
        if (!Current(work.Run, snapshot)) { throw new InvalidOperationException("The PR or CI revision changed before review."); }
        var evidence = work.Run.Evidence ?? await source.GetReviewEvidenceAsync(binding, snapshot, cancellationToken).ConfigureAwait(true);
        RequireEvidence(evidence, snapshot);
        snapshot = await CurrentSnapshotAsync().ConfigureAwait(true);
        var ledger = GrainFactory.GetGrain<IReviewLedger>(work.Inbox.ToGrainId());
        if (!await ledger.StoreEvidenceAsync(work.Run.Id, work.Run.Generation, evidence, snapshot).ConfigureAwait(true)) { return; }
        cancellationToken.ThrowIfCancellationRequested();

        // Start both source-bound requests before awaiting either. Neither passes through
        // the owner root, Ino or the single repository activation.
        var architecture = RunRoleAsync<IArchitectureReviewer>("architecture", work.Run.Architecture,
            work.Run.ArchitectureRequest);
        var quality = RunRoleAsync<ICodeQualityReviewer>("code-quality", work.Run.CodeQuality,
            work.Run.CodeQualityRequest);
        await Task.WhenAll(architecture, quality).ConfigureAwait(true);

        async Task RunRoleAsync<TAgent>(string role, ReviewRoleResult? prior, AgentRequest instructions) where TAgent : IAgent
        {
            if (prior?.Status == "completed") { return; }
            ReviewRoleResult result;
            try
            {
                var name = PrincipalPartition.InstanceName(work.Actor.PrincipalId,
                    $"review-{work.Run.Id:N}-{work.Run.Generation}-{role}");
                var target = NeuronId.For<TAgent>(Id.Owner, name);
                var request = new AgentRequest($"{instructions.Text}\n\nPR #{snapshot.Number}; head {snapshot.HeadSha}; base {snapshot.BaseSha}; CI {snapshot.CiSha}; evidence SHA256 {evidence.Hash}.\n"
                    + $"BEGIN UNTRUSTED PINNED EVIDENCE\n{evidence.Text}\nEND UNTRUSTED PINNED EVIDENCE");
                var reply = await RequestAsync<AgentReply>(target, request, cancellationToken).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(reply.Text) || Encoding.UTF8.GetByteCount(reply.Text) > 32 * 1024)
                {
                    throw new InvalidOperationException("The review result was empty or exceeded its output bound.");
                }
                result = new(role, "completed", reply.Text, work.Run.Attempts);
            }
            catch (OperationCanceledException) { result = new(role, "cancelled", null, work.Run.Attempts, "Review cancelled or deadline exceeded."); }
            catch (Exception) { result = new(role, "failed", null, work.Run.Attempts, "The reviewer did not produce a complete result."); }
            var current = await CurrentSnapshotAsync().ConfigureAwait(true);
            await ledger.StoreRoleAsync(work.Run.Id, work.Run.Generation, result, current).ConfigureAwait(true);
        }

        async Task<PullRequestSnapshot> CurrentSnapshotAsync()
        {
            var currentBinding = bindings.Get(work.BindingId, work.Actor.PrincipalId, Id.Owner);
            if (currentBinding.Revision != work.Run.BindingRevision) { throw new InvalidOperationException("The review binding changed."); }
            var current = await source.GetPullRequestAsync(currentBinding, work.Run.Snapshot.Number, cancellationToken).ConfigureAwait(true);
            currentBinding.Authorize(Id.Owner, work.Actor.PrincipalId);
            if (!Current(work.Run, current)) { throw new InvalidOperationException("The pinned PR or CI is no longer current."); }
            return current;
        }
    }

    internal static bool Current(ReviewRun run, PullRequestSnapshot snapshot)
        => snapshot.RepositoryId == run.Snapshot.RepositoryId && snapshot.Number == run.Snapshot.Number
            && snapshot.HeadSha == run.Snapshot.HeadSha && snapshot.BaseSha == run.Snapshot.BaseSha
            && snapshot.Revision == run.Snapshot.Revision && snapshot.CiRevision == run.Snapshot.CiRevision
            && snapshot.CiSha == run.Snapshot.CiSha
            && GitHubReviewPolicy.ChecksSucceeded(snapshot, run.RequiredChecks, run.AcceptedConclusions);

    internal static void RequireEvidence(GitHubReviewEvidence evidence, PullRequestSnapshot snapshot)
    {
        if (!evidence.Complete || evidence.HeadSha != snapshot.HeadSha || evidence.BaseSha != snapshot.BaseSha
            || string.IsNullOrWhiteSpace(evidence.Text) || Encoding.UTF8.GetByteCount(evidence.Text) > 128 * 1024
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.Text))), evidence.Hash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Complete bounded evidence matching the pinned revision is required.");
        }
    }
}
