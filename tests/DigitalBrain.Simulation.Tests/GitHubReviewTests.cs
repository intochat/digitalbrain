using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk.Webhooks;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubReviewTests
{
    [Fact]
    public async Task Signed_fact_waits_for_green_then_two_distinct_reviewers_enter_before_either_finishes_and_publish_once()
    {
        await using var scenario = await ReviewScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        await scenario.EnableAsync();
        await scenario.WaitForSubscriptionsAsync();
        await scenario.DeliverAsync();
        await scenario.WaitForCandidateAsync();
        var waiting = await scenario.StartReviewAsync();
        Assert.Equal("waiting", waiting.Status);
        Assert.Empty(scenario.Model.Requests);
        scenario.Source.Green();
        await scenario.DeliverAsync();
        await scenario.WaitForCandidateAsync("green");
        var admitted = await scenario.StartReviewAsync();
        var duplicate = await scenario.StartReviewAsync();
        Assert.Equal(admitted.RunId, duplicate.RunId);
        await scenario.Model.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken);
        Assert.Equal(2, scenario.Model.Requests.Count);
        Assert.Equal(2, scenario.Model.Requests.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(scenario.Model.Requests.Values, text => Assert.Contains(scenario.Source.Evidence.Text, text, StringComparison.Ordinal));
        Assert.All(scenario.Model.Requests.Values, text => Assert.Contains(scenario.Source.Evidence.Hash, text, StringComparison.Ordinal));
        var before = await scenario.ResultsAsync();
        Assert.Equal("running", Assert.Single(before.Results).Status);
        scenario.Model.Release.TrySetResult();
        await EventuallyAsync(async () => (await scenario.ResultsAsync()).Results.Single().Status == "completed");
        var result = (await scenario.ResultsAsync()).Results.Single();
        Assert.Equal("completed", result.Architecture?.Status);
        Assert.Equal("completed", result.CodeQuality?.Status);
        Assert.Equal(scenario.Source.Evidence.Hash, result.EvidenceHash);
        var text = $"PR #{result.Snapshot.Number} at {result.Snapshot.HeadSha}\nArchitecture: {result.Architecture!.Text}\nQuality: {result.CodeQuality!.Text}";
        await scenario.Inbox.RequestAsync(new PublishPullRequestReview(result.RunId, text), TestContext.Current.CancellationToken);
        await EventuallyAsync(async () => (await scenario.ResultsAsync()).Results.Single().Published);
        await scenario.Inbox.RequestAsync(new PublishPullRequestReview(result.RunId, text), TestContext.Current.CancellationToken);
        var transcript = await ChatTranscriptRead.ForAsync(scenario.Simulation.Brain, scenario.Chat.Name, TestContext.Current.CancellationToken);
        Assert.Single(transcript.Turns, turn => turn.Text == text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Inbox.RequestAsync(new PublishPullRequestReview(result.RunId, text + " altered"), TestContext.Current.CancellationToken));
        var incoming = await scenario.Query(scenario.Inbox.Id).ReadJournal(JournalKind.Incoming, 0);
        Assert.Contains(incoming.Delta, delivery => delivery.Signal is PullRequestOpened && delivery.Caller == scenario.Repository.Id);
        var architecture = NeuronId.For<IArchitectureReviewer>(scenario.Binding.Owner,
            PrincipalPartition.InstanceName(scenario.Actor.PrincipalId, $"review-{admitted.RunId:N}-1-architecture"));
        var quality = NeuronId.For<ICodeQualityReviewer>(scenario.Binding.Owner,
            PrincipalPartition.InstanceName(scenario.Actor.PrincipalId, $"review-{admitted.RunId:N}-1-code-quality"));
        var architectureRequest = Assert.Single((await scenario.Query(architecture).ReadJournal(JournalKind.Incoming, 0)).Delta, delivery => delivery.Signal is AgentRequest);
        var qualityRequest = Assert.Single((await scenario.Query(quality).ReadJournal(JournalKind.Incoming, 0)).Delta, delivery => delivery.Signal is AgentRequest);
        Assert.Equal(architectureRequest.Caller, qualityRequest.Caller);
        Assert.Equal("github-review-worker", architectureRequest.Caller.Type);
        Assert.NotEqual(architecture, quality);
    }

    [Fact]
    public async Task Changed_head_invalidates_an_active_generation_and_late_role_results_cannot_be_published()
    {
        await using var scenario = await ReviewScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        await scenario.ReadyAsync();
        var admitted = await scenario.StartReviewAsync();
        await scenario.Model.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken);
        scenario.Source.Snapshot = scenario.Source.Snapshot with { HeadSha = new string('c', 40), CiSha = new string('c', 40),
            Revision = "new-head", CiRevision = "new-ci", Checks = [], ObservedAt = DateTimeOffset.UtcNow };
        await scenario.DeliverAsync();
        await EventuallyAsync(async () => (await scenario.ResultsAsync()).Results.Single().Status == "superseded");
        scenario.Model.Release.TrySetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var rejected = await scenario.Inbox.RequestAsync(new PublishPullRequestReview(admitted.RunId, "stale output"), TestContext.Current.CancellationToken);
        Assert.Equal("rejected", rejected.Status);
        Assert.False((await scenario.ResultsAsync()).Results.Single().Published);
    }

    [Fact]
    public async Task Authoritative_red_or_incomplete_evidence_blocks_models_even_after_green_candidate_admission()
    {
        await using var scenario = await ReviewScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        await scenario.ReadyAsync();
        scenario.Source.ReturnRed = true;
        _ = await scenario.StartReviewAsync();
        await EventuallyAsync(async () => (await scenario.ResultsAsync()).Results.Single().Status == "superseded");
        Assert.Empty(scenario.Model.Requests);
    }

    [Fact]
    public async Task Missing_role_retries_independently_and_completed_sibling_is_retained()
    {
        await using var scenario = await ReviewScenario.StartAsync(failQualityOnce: true);
        using var actor = VerifiedActor.Enter(scenario.Actor);
        scenario.Model.Release.TrySetResult();
        await scenario.ReadyAsync();
        _ = await scenario.StartReviewAsync();
        await EventuallyAsync(async () => (await scenario.ResultsAsync()).Results.Single().Status == "completed", seconds: 35);
        Assert.Equal(1, scenario.Model.ArchitectureCalls);
        Assert.Equal(2, scenario.Model.QualityCalls);
        var result = (await scenario.ResultsAsync()).Results.Single();
        Assert.Equal(1, result.Architecture?.Attempt);
        Assert.Equal(2, result.CodeQuality?.Attempt);
    }

    [Fact]
    public async Task Disable_removes_bound_edges_ignores_late_facts_and_same_revision_restart_does_not_move_enable_boundary()
    {
        await using var scenario = await ReviewScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        await scenario.EnableAsync();
        await scenario.WaitForSubscriptionsAsync();
        await scenario.DeliverAsync();
        await scenario.WaitForCandidateAsync();
        await scenario.Inbox.RequestAsync(new EnablePullRequestReview(scenario.Binding.Id, scenario.Behavior.Name,
            scenario.Behavior.Revision, DateTimeOffset.UtcNow.AddSeconds(1)), TestContext.Current.CancellationToken);
        Assert.Single((await scenario.Inbox.RequestAsync(new ReadReviewCandidates(), TestContext.Current.CancellationToken)).Candidates);
        await scenario.Inbox.RequestAsync(new DisablePullRequestReview(), TestContext.Current.CancellationToken);
        await EventuallyAsync(async () => !(await scenario.Query(scenario.Repository.Id).ReadSynapses()).Any(edge => edge.Target == scenario.Inbox.Id && edge.Kind == SynapseKind.Bound));
        scenario.Source.Green();
        await scenario.DeliverAsync();
        var candidates = await scenario.Inbox.RequestAsync(new ReadReviewCandidates(), TestContext.Current.CancellationToken);
        Assert.False(candidates.Enabled);
        Assert.Equal("pending", Assert.Single(candidates.Candidates).CiRevision);
        Assert.Equal("rejected", (await scenario.StartReviewAsync()).Status);
        Assert.Empty(scenario.Model.Requests);
    }

    [Fact]
    public async Task Removing_the_admitted_behavior_disables_work_without_relying_on_script_finally()
    {
        await using var scenario = await ReviewScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        await scenario.EnableAsync();
        await scenario.WaitForSubscriptionsAsync();
        await scenario.Simulation.Brain.Get<IBehaviors>().SendAsync(new RemoveBehavior(scenario.Behavior.Name), TestContext.Current.CancellationToken);
        await EventuallyAsync(async () => !(await scenario.Inbox.RequestAsync(new ReadReviewCandidates(), TestContext.Current.CancellationToken)).Enabled);
        Assert.Empty(scenario.Model.Requests);
    }

    [Fact]
    public void Recovery_fences_interrupted_attempt_and_retains_completed_role_and_immutable_evidence()
    {
        var completed = new ReviewRoleResult("architecture", "completed", "retained architecture result", 1);
        var run = new ReviewRun { Id = Guid.NewGuid(), Status = "running", Generation = 7, Attempts = 1, MaxAttempts = 2,
            Architecture = completed, Evidence = new("head", "base", "evidence", "hash", true) };
        var recovered = PullRequestReview.RecoverInterrupted(new ReviewState { Enabled = true, Runs = [run] });
        var resumed = Assert.Single(recovered.Runs);
        Assert.Equal("pending", resumed.Status);
        Assert.Equal(8, resumed.Generation);
        Assert.Equal(completed, resumed.Architecture);
        Assert.Equal(run.Evidence, resumed.Evidence);
        Assert.Equal(run.Id, resumed.Id);
        Assert.Null(resumed.CodeQuality);
        var exhausted = PullRequestReview.RecoverInterrupted(new ReviewState { Runs = [run with { Attempts = 2 }] });
        Assert.Equal("failed", Assert.Single(exhausted.Runs).Status);
    }

    [Fact]
    public async Task A_new_silo_restores_admission_candidates_subscriptions_and_retries_only_the_missing_role()
    {
        var journal = new VolatileJournalStorageProvider(Options.Create(new JournaledStateManagerOptions()));
        var first = await ReviewScenario.StartAsync(failQualityOnce: true, journal: journal);
        var binding = first.Binding;
        var source = first.Source;
        var model = first.Model;
        Guid runId;
        Guid behaviorRevision;
        string evidenceHash;
        try
        {
            using var actor = VerifiedActor.Enter(first.Actor);
            model.Release.TrySetResult();
            await first.ReadyAsync();
            runId = (await first.StartReviewAsync()).RunId;
            await EventuallyAsync(async () =>
            {
                var result = (await first.ResultsAsync()).Results.Single();
                return result.Status == "pending" && result.Architecture?.Status == "completed" && result.CodeQuality?.Status == "failed";
            });
            var persisted = (await first.ResultsAsync()).Results.Single();
            behaviorRevision = first.Behavior.Revision;
            evidenceHash = Assert.IsType<string>(persisted.EvidenceHash);
            Assert.Equal(1, model.ArchitectureCalls);
            Assert.Equal(1, model.QualityCalls);
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using var restored = await ReviewScenario.StartAsync(journal: journal,
            restoredBinding: binding, restoredSource: source, restoredModel: model, admitBehavior: false);
        using var resumedActor = VerifiedActor.Enter(restored.Actor);
        // No Enable command, subscription command, webhook, or candidate/run write is repeated.
        Assert.Equal(behaviorRevision, restored.Behavior.Revision);
        var candidates = await restored.Inbox.RequestAsync(new ReadReviewCandidates(), TestContext.Current.CancellationToken);
        Assert.True(candidates.Enabled);
        Assert.Single(candidates.Candidates);
        await restored.WaitForSubscriptionsAsync();
        await EventuallyAsync(async () => (await restored.ResultsAsync()).Results.Single().Status == "completed", seconds: 35);
        var result = (await restored.ResultsAsync()).Results.Single();
        Assert.Equal(runId, result.RunId);
        Assert.Equal(evidenceHash, result.EvidenceHash);
        Assert.Equal(1, model.ArchitectureCalls);
        Assert.Equal(2, model.QualityCalls);
        Assert.Equal(1, result.Architecture?.Attempt);
        Assert.Equal(2, result.CodeQuality?.Attempt);
    }

    [Fact]
    public void CI_gate_rejects_empty_missing_old_sha_wrong_producer_and_unapproved_conclusions()
    {
        var source = new ReviewSource();
        source.Green();
        var snapshot = source.Snapshot;
        Assert.True(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build", 99)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build", 100)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { ChecksComplete = false }, [new("build", 99)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot with { CiSha = "old" }, [new("build", 99)], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("missing")], ["success"]));
        Assert.False(GitHubReviewPolicy.ChecksSucceeded(snapshot, [new("build")], ["failure"]));
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, int seconds = 25)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < until, "The durable review did not reach the expected state.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ReviewScenario : IAsyncDisposable
    {
        private ReviewScenario(BrainSimulation simulation, GitHubRepositoryBinding binding, ReviewSource source, BarrierChatClient model)
        {
            Simulation = simulation; Binding = binding; Source = source; Model = model;
            Actor = new(binding.Principal, "review-owner");
        }
        internal BrainSimulation Simulation { get; }
        internal GitHubRepositoryBinding Binding { get; }
        internal ReviewSource Source { get; }
        internal BarrierChatClient Model { get; }
        internal ActorContext Actor { get; }
        internal BehaviorDefinition Behavior { get; private set; } = null!;
        internal NeuronReference<IRepository> Repository => Simulation.Brain.Get<IRepository>(Binding.InstanceName);
        internal NeuronReference<IPullRequestReview> Inbox => Simulation.Brain.Get<IPullRequestReview>(GitHubReviewNames.InstanceName(Actor.PrincipalId, Binding.Id, Behavior.Name));
        internal NeuronId Chat => NeuronId.For<IChat>(Binding.Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "review-results"));
        internal INeuronQuery Query(NeuronId id) => Simulation.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

        internal static async Task<ReviewScenario> StartAsync(bool failQualityOnce = false,
            IJournalStorageProvider? journal = null, GitHubRepositoryBinding? restoredBinding = null,
            ReviewSource? restoredSource = null, BarrierChatClient? restoredModel = null, bool admitBehavior = true)
        {
            var binding = restoredBinding ?? new GitHubRepositoryBinding("reviews", new OwnerId(DigitalBrainNames.DefaultOwner), PrincipalId.New(),
                42, 43, 44, "owner", "repository", "fixture-key", "fixture-webhook-secret");
            binding.CompleteRecovery();
            var source = restoredSource ?? new ReviewSource();
            var model = restoredModel ?? new BarrierChatClient(failQualityOnce);
            var simulation = await BrainSimulation.StartAsync(new()
            {
                Modules = new ModuleManifest([typeof(MicrosoftModule), typeof(AIModule), typeof(DigitalBrain.UI.UIModule), typeof(DigitalBrain.Execution.ExecutionModule)]),
                Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
                ConfigureSilo = silo =>
                {
                    silo.Services.AddSingleton(new GitHubRepositoryBindings([binding]));
                    silo.Services.AddSingleton<IGitHubRepositorySource>(source);
                    silo.Services.AddSingleton<IChatClient>(model);
                    if (journal is not null) { silo.Services.AddSingleton(journal); }
                },
            });
            var scenario = new ReviewScenario(simulation, binding, source, model);
            using var actor = VerifiedActor.Enter(scenario.Actor);
            var name = PrincipalPartition.InstanceName(scenario.Actor.PrincipalId, "pr-review");
            if (admitBehavior)
            {
                await simulation.Brain.Get<IBehaviors>().SendAsync(new AdmitBehavior(name, "return 0;"), TestContext.Current.CancellationToken);
            }
            scenario.Behavior = Assert.Single((await simulation.Brain.Get<IBehaviors>().RequestAsync(new ReadBehaviors(), TestContext.Current.CancellationToken)).Behaviors);
            return scenario;
        }

        internal Task<ReviewConfiguration> EnableAsync() => Inbox.RequestAsync(new EnablePullRequestReview(Binding.Id, Behavior.Name,
            Behavior.Revision, Source.Snapshot.CreatedAt.AddMinutes(-1)), TestContext.Current.CancellationToken);
        internal Task<ReviewResults> ResultsAsync() => Inbox.RequestAsync(new ReadReviewResults(), TestContext.Current.CancellationToken);
        internal Task<ReviewAdmission> StartReviewAsync() => Inbox.RequestAsync(new StartPullRequestReview(Source.Snapshot, Behavior.Revision,
            [new("build", 99)], ["success"], new("Architecture custom policy"), new("Code quality custom policy"), Chat), TestContext.Current.CancellationToken);
        internal async Task ReadyAsync()
        {
            Source.Green();
            await EnableAsync();
            await WaitForSubscriptionsAsync();
            await DeliverAsync();
            await WaitForCandidateAsync("green");
        }
        internal Task WaitForSubscriptionsAsync() => EventuallyAsync(async () => (await Query(Repository.Id).ReadSynapses()).Count(edge => edge.Target == Inbox.Id && edge.Kind == SynapseKind.Bound) == 5);
        internal Task WaitForCandidateAsync(string ci = "pending") => EventuallyAsync(async () => (await Inbox.RequestAsync(new ReadReviewCandidates(), TestContext.Current.CancellationToken)).Candidates.Any(candidate => candidate.CiRevision == ci));
        internal async Task DeliverAsync()
        {
            var delivery = Guid.NewGuid().ToString("N");
            var body = JsonSerializer.SerializeToUtf8Bytes(new { action = "opened", number = 1, installation = new { id = 43 },
                repository = new { id = 42, name = "repository", owner = new { login = "owner" } } });
            var request = new WebhookRequest(body, new Dictionary<string, string[]>
            {
                ["X-GitHub-Delivery"] = [delivery], ["X-GitHub-Event"] = ["pull_request"],
                ["X-Hub-Signature-256"] = ["sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Binding.WebhookSecret), body))],
            });
            Assert.Equal(WebhookAcceptance.Accepted, await new GitHubWebhookHandler(Binding, Simulation.Grains).HandleAsync(request, TestContext.Current.CancellationToken));
            var receiptInbox = Simulation.Grains.GetGrain<IGitHubWebhookInbox>(Binding.Id);
            var receipt = Assert.Single(await receiptInbox.ReadPendingAsync(), item => item.DeliveryId == delivery);
            var dispatcher = new NeuronId("github-dispatcher", Binding.Owner, Binding.InstanceName);
            await Simulation.Grains.GetGrain<IGitHubRepositoryDispatcher>(dispatcher.ToGrainId()).DispatchAsync(Binding.Id, receipt, TestContext.Current.CancellationToken);
            await receiptInbox.CompleteAsync(delivery, receipt.Digest);
        }
        public ValueTask DisposeAsync()
        {
            Model.Release.TrySetResult();
            return Simulation.DisposeAsync();
        }
    }

    private sealed class ReviewSource : IGitHubRepositorySource
    {
        internal PullRequestSnapshot Snapshot { get; set; } = new(1, "Review example", "https://github.com/owner/repository/pull/1", true, false,
            new string('a', 40), new string('b', 40), null, new string('a', 40), [], true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "revision", "pending", 42);
        internal bool ReturnRed;
        internal GitHubReviewEvidence Evidence
        {
            get
            {
                const string text = "diff --git a/Neuron.cs b/Neuron.cs\n@@ -1 +1 @@\n-old line\n+new line\n";
                return new(Snapshot.HeadSha, Snapshot.BaseSha, text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), true);
            }
        }
        internal void Green() => Snapshot = Snapshot with { CiRevision = "green", ObservedAt = DateTimeOffset.UtcNow,
            Checks = [new("build", 99, "check", "completed", "success", Snapshot.HeadSha, "1", DateTimeOffset.UtcNow)] };
        public Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepositoryBinding binding, int number, CancellationToken cancellationToken)
        {
            binding.Authorize(binding.Owner, binding.Principal);
            return Task.FromResult(ReturnRed ? Snapshot with { CiRevision = "red", Checks = [] } : Snapshot);
        }
        public Task<IReadOnlyList<PullRequestSnapshot>> ListOpenPullRequestsAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PullRequestSnapshot>>([Snapshot]);
        public Task<GitHubReviewEvidence> GetReviewEvidenceAsync(GitHubRepositoryBinding binding, PullRequestSnapshot snapshot, CancellationToken cancellationToken)
            => Task.FromResult(Evidence);
    }

    private sealed class BarrierChatClient(bool failQualityOnce) : IChatClient
    {
        internal TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentDictionary<string, string> Requests { get; } = new(StringComparer.Ordinal);
        internal int ArchitectureCalls;
        internal int QualityCalls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var materialized = messages.ToArray();
            Assert.Empty(options?.Tools ?? []);
            var architecture = materialized.Any(message => message.Role == ChatRole.System && message.Text.StartsWith("You are the architecture reviewer", StringComparison.Ordinal));
            var role = architecture ? "architecture" : "quality";
            var attempt = architecture ? Interlocked.Increment(ref ArchitectureCalls) : Interlocked.Increment(ref QualityCalls);
            Requests[role] = Assert.Single(materialized, message => message.Role == ChatRole.User).Text;
            if (ArchitectureCalls > 0 && QualityCalls > 0) { BothEntered.TrySetResult(); }
            await BothEntered.Task.WaitAsync(cancellationToken);
            await Release.Task.WaitAsync(cancellationToken);
            if (!architecture && failQualityOnce && attempt == 1) { throw new IOException("Fixture quality failure"); }
            yield return new ChatResponseUpdate(ChatRole.Assistant, $"{role}: reviewed the pinned evidence; no actionable findings.") { FinishReason = ChatFinishReason.Stop };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
