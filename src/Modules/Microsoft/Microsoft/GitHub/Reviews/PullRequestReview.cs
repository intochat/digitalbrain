using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Microsoft.GitHub;

// Facts are admitted quickly into durable state. A tracked worker call runs outside
// the inbox's request turn; all ledger mutation resumes on its owning scheduler.
[GrainType("pullrequestreview")]
internal sealed class PullRequestReview : Neuron, IPullRequestReview, IReviewLedger
{
    private const int Capacity = 128;
    private readonly IDurableValue<byte[]> _storage;
    private readonly Serializer<ReviewState> _serializer;
    private readonly GitHubRepositoryBindings _bindings;
    private readonly IGitHubRepositorySource _source;
    private CancellationTokenSource? _cancellation;
    private Task? _call;
    private Guid? _activeRun;
    private IGrainTimer? _timer;
    private Task? _maintenance;
    private Task? _synchronizing;
    private readonly Dictionary<Guid, Task> _publishing = [];

    public PullRequestReview(NeuronRuntime runtime, GitHubRepositoryBindings bindings, IGitHubRepositorySource source) : base(runtime)
    {
        _storage = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>("github.review-state");
        _serializer = ServiceProvider.GetRequiredService<Serializer<ReviewState>>();
        _bindings = bindings;
        _source = source;
    }

    private ReviewState Load() => _storage.Value is { Length: > 0 } bytes ? _serializer.Deserialize(bytes) : new();
    private async Task CommitAsync(ReviewState state)
    {
        var previous = _storage.Value;
        var bytes = _serializer.SerializeToArray(state);
        _storage.Value = bytes;
        try { await WriteStateAsync().ConfigureAwait(true); }
        catch
        {
            // A failed persistence attempt cannot become an acknowledged in-memory
            // admission on replay. Do not roll back a newer owning-scheduler commit.
            if (ReferenceEquals(_storage.Value, bytes)) { _storage.Value = previous; }
            throw;
        }
    }

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        var state = RecoverInterrupted(Load());
        await CommitAsync(state).ConfigureAwait(true);
        EnsureTimer();
    }

    internal static ReviewState RecoverInterrupted(ReviewState persisted)
    {
        var state = persisted with { Runs = [.. persisted.Runs] };
        for (var index = 0; index < state.Runs.Count; index++)
        {
            var run = state.Runs[index];
            if (run.Status == "running")
            {
                state.Runs[index] = run with { Generation = run.Generation + 1,
                    Status = run.Attempts < run.MaxAttempts ? "pending" : "failed", Detail = "The previous worker was interrupted; completed roles were retained." };
            }
        }
        return state;
    }

    public async Task HandleAsync(EnablePullRequestReview signal, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var binding = _bindings.Find(signal.BindingId) ?? throw new InvalidOperationException("The repository binding is not configured.");
        if (binding.Owner != Id.Owner || binding.Principal != actor.PrincipalId || binding.RecoveryComplete && !binding.Enabled)
        {
            throw new NeuronAuthorizationException("The repository binding is unavailable to this principal.");
        }
        if (string.IsNullOrWhiteSpace(signal.BehaviorName) || signal.BehaviorName.Length > 256
            || signal.BehaviorRevision == Guid.Empty || signal.ObserveAfter > TimeProvider.GetUtcNow().AddMinutes(1))
        {
            throw new ArgumentException("A current named behavior revision and valid observation boundary are required.");
        }
        if (Id.Name != GitHubReviewNames.InstanceName(actor.PrincipalId, signal.BindingId, signal.BehaviorName))
        {
            throw new ArgumentException("Use GitHubReviewNames.InstanceName for the unique binding/behavior inbox.");
        }
        if (!await BehaviorCurrentAsync(signal.BehaviorName, signal.BehaviorRevision, actor.PrincipalId).ConfigureAwait(true))
        {
            throw new InvalidOperationException("The admitted behavior revision does not belong to this principal.");
        }
        var state = Load();
        if (state.BindingId is not null && (state.BindingId != binding.Id || state.BehaviorName != signal.BehaviorName))
        {
            throw new InvalidOperationException("A review inbox belongs to one repository binding and named behavior.");
        }
        if (!state.Enabled || state.BehaviorRevision != signal.BehaviorRevision)
        {
            FenceAll(state, "cancelled", "The behavior activation changed.");
            state = state with { Enabled = true, BindingId = binding.Id, BehaviorName = signal.BehaviorName,
                BehaviorRevision = signal.BehaviorRevision, ObserveAfter = signal.ObserveAfter, Actor = actor,
                Candidates = [], RemoveSubscriptions = false };
            await CommitAsync(state).ConfigureAwait(true);
            _cancellation?.Cancel();
        }
        EnsureTimer();
        await ReplyAsync(Configuration(state)).ConfigureAwait(true);
    }

    public async Task HandleAsync(DisablePullRequestReview signal, CancellationToken cancellationToken)
    {
        RequireActor();
        await DisableAsync("The behavior was disabled.").ConfigureAwait(true);
        await ReplyAsync(Configuration(Load())).ConfigureAwait(true);
    }

    public Task HandleAsync(PullRequestOpened signal, CancellationToken cancellationToken)
        => AdmitCandidateAsync(signal.BindingId, signal.Repository, signal.Snapshot, cancellationToken);
    public Task HandleAsync(PullRequestUpdated signal, CancellationToken cancellationToken)
        => AdmitCandidateAsync(signal.BindingId, signal.Repository, signal.Snapshot, cancellationToken);
    public Task HandleAsync(PullRequestClosed signal, CancellationToken cancellationToken)
        => AdmitCandidateAsync(signal.BindingId, signal.Repository, signal.Snapshot, cancellationToken);
    public Task HandleAsync(PullRequestChecksChanged signal, CancellationToken cancellationToken)
        => AdmitCandidateAsync(signal.BindingId, signal.Repository, signal.Snapshot, cancellationToken);

    public async Task HandleAsync(RepositoryAccessRevoked signal, CancellationToken cancellationToken)
    {
        var state = Load();
        if (!state.Enabled || state.BindingId != signal.BindingId || state.Actor?.PrincipalId != VerifiedActor.Current?.PrincipalId) { return; }
        var binding = _bindings.Find(signal.BindingId);
        if (binding is null || signal.Repository != RepositoryId(binding)) { return; }
        await DisableAsync("Repository access was revoked.").ConfigureAwait(true);
    }

    private async Task AdmitCandidateAsync(string bindingId, NeuronId repository, PullRequestSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = Load();
        if (!state.Enabled || state.BindingId != bindingId || state.Actor?.PrincipalId != VerifiedActor.Current?.PrincipalId) { return; }
        var binding = _bindings.Find(bindingId);
        if (binding is null || !binding.Enabled || repository != RepositoryId(binding) || snapshot.RepositoryId != binding.RepositoryId) { return; }
        var index = state.Candidates.FindIndex(item => item.Number == snapshot.Number);
        if (index >= 0 && state.Candidates[index].ObservedAt > snapshot.ObservedAt) { return; }
        if (index < 0)
        {
            if (snapshot.CreatedAt < state.ObserveAfter || !snapshot.IsOpen) { return; }
            if (state.Candidates.Count >= Capacity) { throw new InvalidOperationException("The review candidate inbox is full."); }
            state.Candidates.Add(snapshot);
        }
        else { state.Candidates[index] = snapshot; }
        var cancel = false;
        for (var runIndex = 0; runIndex < state.Runs.Count; runIndex++)
        {
            var run = state.Runs[runIndex];
            if (run.Snapshot.Number == snapshot.Number && run.Status is "pending" or "running" or "completed"
                && !PullRequestReviewWorker.Current(run, snapshot))
            {
                state.Runs[runIndex] = run with { Status = "superseded", Generation = run.Generation + 1,
                    Detail = "The PR or CI changed; these results are not current." };
                cancel |= _activeRun == run.Id;
            }
        }
        await CommitAsync(state).ConfigureAwait(true);
        if (cancel) { _cancellation?.Cancel(); }
    }

    public async Task HandleAsync(ReadReviewCandidates signal, CancellationToken cancellationToken)
    {
        RequireActor();
        var state = Load();
        await ReplyAsync(new ReviewCandidates(state.Enabled, [.. state.Candidates])).ConfigureAwait(true);
    }

    public async Task HandleAsync(StartPullRequestReview signal, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var state = Load();
        if (!state.Enabled || state.BindingId is null || state.BehaviorRevision != signal.BehaviorRevision
            || !await BehaviorCurrentAsync(state.BehaviorName!, state.BehaviorRevision, actor.PrincipalId).ConfigureAwait(true))
        {
            await ReplyAsync(new ReviewAdmission(Guid.Empty, "rejected", "The current behavior is not enabled.")).ConfigureAwait(true);
            return;
        }
        state = Load();
        if (!state.Enabled || state.BindingId is null || state.BehaviorRevision != signal.BehaviorRevision)
        {
            await ReplyAsync(new ReviewAdmission(Guid.Empty, "rejected", "The behavior changed during admission.")).ConfigureAwait(true);
            return;
        }
        if (signal.MaxAttempts is < 1 or > 3 || signal.Destination.Type != "chat" || signal.Destination.Owner != Id.Owner
            || !PrincipalPartition.OwnsInstance(actor.PrincipalId, signal.Destination.Name)
            || string.IsNullOrWhiteSpace(signal.Architecture.Text) || signal.Architecture.Text.Length > 16000
            || string.IsNullOrWhiteSpace(signal.CodeQuality.Text) || signal.CodeQuality.Text.Length > 16000)
        {
            throw new ArgumentException("Bounded role prompts, one to three attempts and a chat owned by this principal are required.");
        }
        var configured = _bindings.Find(state.BindingId);
        if (configured is { RecoveryComplete: false })
        {
            await ReplyAsync(new ReviewAdmission(Guid.Empty, "waiting", "Repository access is being recovered after startup.")).ConfigureAwait(true);
            return;
        }
        var binding = _bindings.Get(state.BindingId, actor.PrincipalId, Id.Owner);
        var candidate = state.Candidates.FirstOrDefault(item => item.Number == signal.Expected.Number);
        if (candidate is null || candidate.RepositoryId != binding.RepositoryId || candidate.CreatedAt < state.ObserveAfter)
        {
            await ReplyAsync(new ReviewAdmission(Guid.Empty, "rejected", "No eligible subscribed candidate exists.")).ConfigureAwait(true);
            return;
        }
        var snapshot = candidate;
        var proposed = new ReviewRun { Snapshot = signal.Expected, RequiredChecks = signal.RequiredChecks,
            AcceptedConclusions = signal.AcceptedConclusions };
        if (!PullRequestReviewWorker.Current(proposed, snapshot) || snapshot.RepositoryId != binding.RepositoryId)
        {
            await ReplyAsync(new ReviewAdmission(Guid.Empty, "waiting", "Current complete CI evidence does not satisfy this behavior's requirements.")).ConfigureAwait(true);
            return;
        }
        // CI reruns of an already-reviewed head/base do not duplicate a successful review;
        // stale or interrupted work can be admitted again under the same logical run identity.
        var existing = state.Runs.FirstOrDefault(run => run.Snapshot.Number == snapshot.Number
            && run.Snapshot.HeadSha == snapshot.HeadSha && run.Snapshot.BaseSha == snapshot.BaseSha
            && run.BehaviorRevision == signal.BehaviorRevision);
        if (existing is not null)
        {
            if (existing.Status == "superseded" && existing.Attempts < existing.MaxAttempts && !existing.Published)
            {
                var replacement = existing with { Snapshot = snapshot, Status = "pending", Generation = existing.Generation + 1,
                    Architecture = null, CodeQuality = null, Evidence = null, Detail = null };
                state.Runs[state.Runs.IndexOf(existing)] = replacement;
                await CommitAsync(state).ConfigureAwait(true);
                existing = replacement;
            }
            await ReplyAsync(new ReviewAdmission(existing.Id, existing.Status, existing.Detail)).ConfigureAwait(true);
            EnsureTimer();
            return;
        }
        if (state.Runs.Count >= Capacity) { throw new InvalidOperationException("The retained review ledger is full; use a new named behavior inbox."); }
        var run = new ReviewRun { Id = Guid.NewGuid(), Snapshot = snapshot, BehaviorRevision = signal.BehaviorRevision,
            BindingRevision = binding.Revision, RequiredChecks = [.. signal.RequiredChecks], AcceptedConclusions = [.. signal.AcceptedConclusions],
            ArchitectureRequest = signal.Architecture, CodeQualityRequest = signal.CodeQuality, Destination = signal.Destination,
            MaxAttempts = signal.MaxAttempts };
        state.Runs.Add(run);
        await CommitAsync(state).ConfigureAwait(true);
        await RecordOutgoingAsync(new PullRequestReviewChanged(run.Id, snapshot.Number, "pending")).ConfigureAwait(true);
        await ReplyAsync(new ReviewAdmission(run.Id, run.Status)).ConfigureAwait(true);
        EnsureTimer();
    }

    public async Task HandleAsync(CancelPullRequestReview signal, CancellationToken cancellationToken)
    {
        RequireActor();
        var state = Load();
        var index = state.Runs.FindIndex(run => run.Id == signal.RunId);
        if (index < 0) { await ReplyAsync(new ReviewAdmission(signal.RunId, "missing")).ConfigureAwait(true); return; }
        var run = state.Runs[index];
        if (run.Status is "pending" or "running")
        {
            state.Runs[index] = run with { Status = "cancelled", Generation = run.Generation + 1, Detail = "Cancelled by the behavior." };
            await CommitAsync(state).ConfigureAwait(true);
            if (_activeRun == run.Id) { _cancellation?.Cancel(); }
        }
        await ReplyAsync(new ReviewAdmission(run.Id, state.Runs[index].Status)).ConfigureAwait(true);
    }

    public async Task HandleAsync(ReadReviewResults signal, CancellationToken cancellationToken)
    {
        RequireActor();
        await ReplyAsync(new ReviewResults([.. Load().Runs.Select(run => run.Result())])).ConfigureAwait(true);
    }

    public async Task HandleAsync(PublishPullRequestReview signal, CancellationToken cancellationToken)
    {
        RequireActor();
        if (string.IsNullOrWhiteSpace(signal.Text) || Encoding.UTF8.GetByteCount(signal.Text) > 96 * 1024)
        {
            throw new ArgumentException("A bounded complete review publication is required.");
        }
        var state = Load();
        var run = state.Runs.FirstOrDefault(item => item.Id == signal.RunId);
        if (run is null || run.Status != "completed" || run.Architecture?.Status != "completed" || run.CodeQuality?.Status != "completed")
        {
            await ReplyAsync(new ReviewAdmission(signal.RunId, "rejected", "Both current role results must be complete before publication.")).ConfigureAwait(true);
            return;
        }
        if (run.PublicationText is not null && run.PublicationText != signal.Text)
        {
            throw new InvalidOperationException("A publication ID cannot be reused with different text.");
        }
        if (!run.Published)
        {
            run = run with { PublicationText = signal.Text };
            state.Runs[state.Runs.FindIndex(item => item.Id == run.Id)] = run;
            await CommitAsync(state).ConfigureAwait(true);
            if (!_publishing.TryGetValue(run.Id, out var call) || call.IsCompleted)
            {
                _publishing[run.Id] = PublishAsync(run.Id);
            }
        }
        await ReplyAsync(new ReviewAdmission(run.Id, run.Published ? "published" : "publication-pending")).ConfigureAwait(true);
    }

    private async Task PublishAsync(Guid runId)
    {
        try
        {
            var state = Load();
            var run = state.Runs.First(item => item.Id == runId);
            using var actor = VerifiedActor.Enter(state.Actor!);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (!await IsCurrentAsync(state, run, deadline.Token).ConfigureAwait(true)) { return; }
            state = Load();
            var current = state.Runs.First(item => item.Id == runId);
            if (!state.Enabled || current.Generation != run.Generation || current.Status != "completed" || current.Published) { return; }
            await RequestAsync<NotePublished>(current.Destination, new PublishNote(current.Id, current.PublicationText!), deadline.Token).ConfigureAwait(true);
            state = Load();
            var index = state.Runs.FindIndex(item => item.Id == runId);
            state.Runs[index] = state.Runs[index] with { Published = true };
            await CommitAsync(state).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The persisted exact text/run ID is retried by the script or activation recovery.
            // Chat's durable publication ID prevents an uncertain response from duplicating it.
        }
    }

    public async Task<bool> StoreEvidenceAsync(Guid runId, int generation, GitHubReviewEvidence evidence, PullRequestSnapshot verifiedSnapshot)
    {
        RequireActor();
        var state = Load();
        var index = state.Runs.FindIndex(run => run.Id == runId && run.Generation == generation && run.Status == "running");
        if (index < 0 || !state.Enabled) { return false; }
        var run = state.Runs[index];
        PullRequestReviewWorker.RequireEvidence(evidence, run.Snapshot);
        if (!await MayCommitAsync(state, run, verifiedSnapshot).ConfigureAwait(true)) { return false; }
        state = Load();
        index = state.Runs.FindIndex(item => item.Id == runId && item.Generation == generation && item.Status == "running");
        if (!state.Enabled || index < 0) { return false; }
        run = state.Runs[index];
        if (run.Evidence is not null && run.Evidence.Hash != evidence.Hash) { throw new InvalidOperationException("Pinned review evidence changed."); }
        state.Runs[index] = run with { Evidence = evidence };
        await CommitAsync(state).ConfigureAwait(true);
        return true;
    }

    public async Task StoreRoleAsync(Guid runId, int generation, ReviewRoleResult result, PullRequestSnapshot verifiedSnapshot)
    {
        RequireActor();
        var state = Load();
        var index = state.Runs.FindIndex(run => run.Id == runId && run.Generation == generation && run.Status == "running");
        if (index < 0 || !state.Enabled || result.Role is not ("architecture" or "code-quality")) { return; }
        var run = state.Runs[index];
        if (result.Attempt != run.Attempts || !await MayCommitAsync(state, run, verifiedSnapshot).ConfigureAwait(true)) { return; }
        state = Load();
        index = state.Runs.FindIndex(item => item.Id == runId && item.Generation == generation && item.Status == "running");
        if (!state.Enabled || index < 0) { return; }
        run = state.Runs[index];
        if (result.Role == "architecture" && run.Architecture?.Status != "completed") { run = run with { Architecture = result }; }
        if (result.Role == "code-quality" && run.CodeQuality?.Status != "completed") { run = run with { CodeQuality = result }; }
        if (run.Architecture?.Status == "completed" && run.CodeQuality?.Status == "completed") { run = run with { Status = "completed", Detail = null }; }
        state.Runs[index] = run;
        await CommitAsync(state).ConfigureAwait(true);
        await RecordOutgoingAsync(new PullRequestReviewChanged(run.Id, run.Snapshot.Number, result.Status, result.Role)).ConfigureAwait(true);
    }

    private void EnsureTimer() => _timer ??= this.RegisterGrainTimer(WakeAsync, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));

    private Task WakeAsync(CancellationToken cancellationToken)
    {
        if (_maintenance is null || _maintenance.IsCompleted) { _maintenance = MaintainAsync(cancellationToken); }
        return Task.CompletedTask;
    }

    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        try { await ReconcileAsync(cancellationToken).ConfigureAwait(true); }
        catch (Exception) { /* State remains authoritative; the bounded timer retries transient reads. */ }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var state = Load();
        if (state.Actor is null) { return; }
        using var actor = VerifiedActor.Enter(state.Actor);
        if ((state.Enabled || state.RemoveSubscriptions) && (_synchronizing is null || _synchronizing.IsCompleted))
        {
            _synchronizing = SynchronizeAsync(state);
        }
        if (!state.Enabled || state.Actor is null) { return; }
        var currentBinding = _bindings.Find(state.BindingId!);
        if (currentBinding is { RecoveryComplete: false }) { return; }
        if (currentBinding is null || !currentBinding.Enabled
            || !await BehaviorCurrentAsync(state.BehaviorName!, state.BehaviorRevision, state.Actor.PrincipalId).ConfigureAwait(true))
        {
            await DisableAsync("The repository binding or admitted behavior was removed or replaced.").ConfigureAwait(true);
            return;
        }
        if (_activeRun is { } active)
        {
            var running = state.Runs.FirstOrDefault(run => run.Id == active);
            if (running?.Status == "running" && (TimeProvider.GetUtcNow() - running.StartedAt > TimeSpan.FromMinutes(3)
                || !await IsCurrentAsync(state, running, cancellationToken).ConfigureAwait(true)))
            {
                state = Load();
                var currentIndex = state.Runs.FindIndex(run => run.Id == running.Id && run.Generation == running.Generation && run.Status == "running");
                if (currentIndex < 0) { return; }
                state.Runs[currentIndex] = state.Runs[currentIndex] with { Status = "superseded", Generation = running.Generation + 1,
                    Detail = "The review deadline or current PR/CI gate no longer permits this attempt." };
                await CommitAsync(state).ConfigureAwait(true);
                _cancellation?.Cancel();
            }
            if (_call is { IsCompleted: false }) { return; }
            _activeRun = null;
            _call = null;
        }
        state = Load();
        var next = state.Runs.FirstOrDefault(run => run.Status == "pending");
        if (next is null) { return; }
        if (!await IsCurrentAsync(state, next, cancellationToken).ConfigureAwait(true))
        {
            state = Load();
            var currentIndex = state.Runs.FindIndex(run => run.Id == next.Id && run.Generation == next.Generation && run.Status == "pending");
            if (currentIndex < 0) { return; }
            state.Runs[currentIndex] = next with { Status = "superseded", Generation = next.Generation + 1,
                Detail = "The PR, CI, binding or behavior changed before execution." };
            await CommitAsync(state).ConfigureAwait(true);
            return;
        }
        state = Load();
        if (!state.Enabled || !state.Runs.Any(run => run.Id == next.Id && run.Generation == next.Generation && run.Status == "pending")) { return; }
        next = next with { Status = "running", Generation = next.Generation + 1, Attempts = next.Attempts + 1, StartedAt = TimeProvider.GetUtcNow() };
        state.Runs[state.Runs.FindIndex(run => run.Id == next.Id)] = next;
        await CommitAsync(state).ConfigureAwait(true);
        await RecordOutgoingAsync(new PullRequestReviewChanged(next.Id, next.Snapshot.Number, "running")).ConfigureAwait(true);
        DelayDeactivation(TimeSpan.FromMinutes(4));
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _activeRun = next.Id;
        _call = RunCallAsync(new ReviewWork(Id, state.BindingId!, state.Actor!, next), _cancellation.Token);
    }

    private async Task SynchronizeAsync(ReviewState observed)
    {
        try
        {
            using var actor = VerifiedActor.Enter(observed.Actor!);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var worker = new NeuronId("github-review-worker", Id.Owner, observed.Actor is { } principal
                ? PrincipalPartition.InstanceName(principal.PrincipalId, "sync-" + Id.Name[33..]) : Id.Name);
            var snapshots = await GrainFactory.GetGrain<IPullRequestReviewWorker>(worker.ToGrainId())
                .SynchronizeAsync(Id, observed.BindingId!, observed.Actor!, observed.Enabled, deadline.Token).ConfigureAwait(true);
            var state = Load();
            if (state.Enabled != observed.Enabled || state.BehaviorRevision != observed.BehaviorRevision) { return; }
            if (!observed.Enabled)
            {
                await CommitAsync(state with { RemoveSubscriptions = false }).ConfigureAwait(true);
                return;
            }
            var binding = _bindings.Find(observed.BindingId!);
            if (binding is null) { return; }
            foreach (var snapshot in snapshots)
            {
                await AdmitCandidateAsync(binding.Id, RepositoryId(binding), snapshot, deadline.Token).ConfigureAwait(true);
            }
        }
        catch (Exception) { /* Binding/reconciliation retries do not erase accepted facts. */ }
    }

    private async Task RunCallAsync(ReviewWork work, CancellationToken cancellationToken)
    {
        string? failure = null;
        try
        {
            var workerId = new NeuronId("github-review-worker", Id.Owner,
                PrincipalPartition.InstanceName(work.Actor.PrincipalId, $"review-{work.Run.Id:N}-{work.Run.Generation}"));
            await GrainFactory.GetGrain<IPullRequestReviewWorker>(workerId.ToGrainId()).RunAsync(work, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { failure = "The review attempt was cancelled or exceeded its deadline."; }
        catch (Exception) { failure = "The review worker could not complete its pinned evidence or reviewer requests."; }
        var state = Load();
        var index = state.Runs.FindIndex(run => run.Id == work.Run.Id && run.Generation == work.Run.Generation && run.Status == "running");
        if (index < 0) { return; }
        var run = state.Runs[index];
        state.Runs[index] = run with { Status = run.Attempts < run.MaxAttempts ? "pending" : "failed",
            Detail = failure ?? "At least one reviewer is incomplete; completed role results are retained." };
        await CommitAsync(state).ConfigureAwait(true);
    }

    private async Task<bool> IsCurrentAsync(ReviewState state, ReviewRun run, CancellationToken cancellationToken)
    {
        if (!state.Enabled || state.Actor is null || state.BehaviorRevision != run.BehaviorRevision) { return false; }
        try
        {
            var binding = _bindings.Get(state.BindingId!, state.Actor.PrincipalId, Id.Owner);
            if (binding.Revision != run.BindingRevision
                || !await BehaviorCurrentAsync(state.BehaviorName!, state.BehaviorRevision, state.Actor.PrincipalId).ConfigureAwait(true)) { return false; }
            var snapshot = await _source.GetPullRequestAsync(binding, run.Snapshot.Number, cancellationToken).ConfigureAwait(true);
            return PullRequestReviewWorker.Current(run, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    private async Task<bool> MayCommitAsync(ReviewState state, ReviewRun run, PullRequestSnapshot workerVerified)
    {
        if (!state.Enabled || state.Actor is null || state.BehaviorRevision != run.BehaviorRevision
            || !PullRequestReviewWorker.Current(run, workerVerified)
            || !state.Candidates.Any(candidate => candidate.Number == run.Snapshot.Number && PullRequestReviewWorker.Current(run, candidate)))
        {
            return false;
        }
        var binding = _bindings.Get(state.BindingId!, state.Actor.PrincipalId, Id.Owner);
        return binding.Revision == run.BindingRevision
            && await BehaviorCurrentAsync(state.BehaviorName!, state.BehaviorRevision, state.Actor.PrincipalId).ConfigureAwait(true);
    }

    private Task<IReadOnlyList<BehaviorDefinition>> CurrentBehaviorsAsync()
        => GrainFactory.GetGrain<IBehaviorsKernel>(NeuronId.For<IBehaviors>(Id.Owner, "default").ToGrainId()).ReadCurrent();

    private async Task<bool> BehaviorCurrentAsync(string name, Guid revision, PrincipalId principal)
        => (await CurrentBehaviorsAsync().ConfigureAwait(true)).Any(behavior => behavior.Name == name && behavior.Revision == revision
            && behavior.Principal == principal);

    private async Task DisableAsync(string detail)
    {
        var state = Load();
        FenceAll(state, "cancelled", detail);
        await CommitAsync(state with { Enabled = false, RemoveSubscriptions = true }).ConfigureAwait(true);
        _cancellation?.Cancel();
        EnsureTimer();
    }

    private static void FenceAll(ReviewState state, string status, string detail)
    {
        for (var index = 0; index < state.Runs.Count; index++)
        {
            var run = state.Runs[index];
            if (run.Status is "pending" or "running" or "completed")
            {
                state.Runs[index] = run with { Status = status, Generation = run.Generation + 1, Detail = detail };
            }
        }
    }

    private ActorContext RequireActor()
    {
        var actor = VerifiedActor.Current ?? throw new NeuronAuthorizationException("An authenticated review principal is required.");
        var state = Load();
        if (!PrincipalPartition.OwnsInstance(actor.PrincipalId, Id.Name)
            || state.Actor is not null && state.Actor.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException("The review inbox belongs to another principal.");
        }
        return actor;
    }

    private static NeuronId RepositoryId(GitHubRepositoryBinding binding) => NeuronId.For<IRepository>(binding.Owner, binding.InstanceName);
    private static ReviewConfiguration Configuration(ReviewState state) => new(state.Enabled, state.BindingId, state.BehaviorName, state.BehaviorRevision);
}
