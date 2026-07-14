using System.Text.Json;
using DigitalBrain.FeatureHost;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Contracts;
using Xunit;
using GrainInput = DigitalBrain.Kernel.Contracts.FeatureInput;

namespace DigitalBrain.UnitTests;

public sealed class FeatureExecutionWorkerTests
{
    private static readonly FeatureInstallationId Installation = new("email-summarizer");

    [Fact]
    public void Handler_deadline_cannot_exceed_sixty_seconds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureExecutionOptions(
            "feature-host-tests",
            TimeSpan.FromSeconds(61),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Worker_commits_only_after_handler_and_disposes_run_context()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var reader = new ImmediateGmailReader();
        await using var manager = await ManagerAsync(release, reader);
        var grain = new RecordingInstallationGrain(Claim(release.Descriptor.Digest));
        var source = new SingleWorkSource(new FeatureWorkItem(Installation, grain));
        var context = new RecordingRunContext();
        var worker = Worker(manager, source, new SingleContextFactory(context));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        var commit = await grain.Committed.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Equal("summary", context.ModelResponse.Text);
        Assert.Single(commit.Intents);
        Assert.Equal(FeatureIntentKind.TextSurface, commit.Intents[0].Kind);
        Assert.True(context.Disposed);
        Assert.Equal(0, grain.Failures);
    }

    [Fact]
    public async Task Worker_activates_constructor_injected_contract_adapters_inside_handler_task()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        await using var manager = await ManagerAsync(release, new FeatureGmailMessageReader());
        var grain = new RecordingInstallationGrain(Claim(release.Descriptor.Digest));
        var source = new SingleWorkSource(new FeatureWorkItem(Installation, grain));
        var contexts = new CapabilityFeatureRunContextFactory(new FeatureCapabilityClient(), TimeProvider.System);
        var worker = Worker(manager, source, contexts);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        var commit = await grain.Committed.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Single(commit.Intents);
        Assert.Contains("summary", commit.Intents[0].PayloadJson, StringComparison.Ordinal);
        Assert.Equal(0, grain.Failures);
    }

    [Fact]
    public async Task Production_context_reserves_exact_limits_before_outbound_work()
    {
        var digest = new ReleaseDigest(new string('a', 64));
        var claim = Claim(digest);
        var work = new FeatureWorkItem(
            Installation,
            new RecordingInstallationGrain(claim));
        var client = new CountingCapabilityClient();
        var factory = new CapabilityFeatureRunContextFactory(client, TimeProvider.System);
        await using var runContext = await factory.CreateAsync(work, claim);

        for (var index = 0; index < 20; index++)
            await runContext.Context.MemoryRecall.RecallAsync(new MemoryRecallRequest("query", []));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runContext.Context.MemoryRecall.RecallAsync(new MemoryRecallRequest("query", [])));
        for (var index = 0; index < 4; index++)
            await runContext.Context.Models.CompleteAsync(new ModelRequest("summary", "prompt", $"model-{index}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runContext.Context.Models.CompleteAsync(new ModelRequest("summary", "prompt", "model-overflow")));
        for (var index = 0; index < 32; index++)
            runContext.Context.Intents.AddTextSurface(new TextSurfaceIntent($"intent-{index}", "title", "text"));
        Assert.Throws<InvalidOperationException>(() =>
            runContext.Context.Intents.AddTextSurface(new TextSurfaceIntent("intent-overflow", "title", "text")));

        Assert.Equal(20, client.Reads);
        Assert.Equal(4, client.Models);
    }

    [Fact]
    public async Task Production_context_atomically_limits_parallel_intents()
    {
        var digest = new ReleaseDigest(new string('a', 64));
        var claim = Claim(digest);
        var work = new FeatureWorkItem(
            Installation,
            new RecordingInstallationGrain(claim));
        var factory = new CapabilityFeatureRunContextFactory(new CountingCapabilityClient(), TimeProvider.System);
        await using var runContext = await factory.CreateAsync(work, claim);

        var attempts = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() => RecordIntent(runContext.Context, index)))
            .ToArray();
        var results = await Task.WhenAll(attempts);
        var commit = await runContext.SealAsync(claim.Fence);

        Assert.Equal(32, results.Count(result => result));
        Assert.Equal(32, commit.Intents.Count);
        Assert.Equal(32, commit.Intents.Select(intent => intent.LogicalOperationKey).Distinct().Count());
    }

    [Fact]
    public async Task Sealing_cancels_and_drains_fire_and_forget_capabilities_before_usage_snapshot()
    {
        var digest = new ReleaseDigest(new string('a', 64));
        var claim = Claim(digest);
        var work = new FeatureWorkItem(
            Installation,
            new RecordingInstallationGrain(claim));
        var client = new BlockingCapabilityClient();
        var factory = new CapabilityFeatureRunContextFactory(client, TimeProvider.System);
        await using var runContext = await factory.CreateAsync(work, claim);

        var abandoned = runContext.Context.Models.CompleteAsync(
            new ModelRequest("summary", "prompt", "abandoned-model"));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var commit = await runContext.SealAsync(claim.Fence);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.Equal(1, commit.Usage.ModelCalls);
        Assert.True(client.Cancelled);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runContext.Context.Models.CompleteAsync(
                new ModelRequest("summary", "prompt", "post-seal-model")));
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Handler_deadline_cancels_feature_and_records_safe_failure_without_commit()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var reader = new BlockingGmailReader();
        await using var manager = await ManagerAsync(release, reader);
        var grain = new RecordingInstallationGrain(Claim(release.Descriptor.Digest));
        var source = new SingleWorkSource(new FeatureWorkItem(Installation, grain));
        var context = new RecordingRunContext();
        var worker = Worker(
            manager,
            source,
            new SingleContextFactory(context),
            new FeatureExecutionOptions(
                "feature-host-tests",
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        var failure = await grain.Failed.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Equal("feature execution deadline exceeded", failure);
        Assert.False(grain.Committed.Task.IsCompleted);
        Assert.True(context.Disposed);
        Assert.True(reader.Cancelled);
    }

    [Fact]
    public async Task Claim_for_nonactive_digest_fails_without_executing_feature()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        await using var manager = await ManagerAsync(release, new ImmediateGmailReader());
        var grain = new RecordingInstallationGrain(Claim(new ReleaseDigest(new string('f', 64))));
        var source = new SingleWorkSource(new FeatureWorkItem(Installation, grain));
        var factory = new CountingContextFactory();
        var worker = Worker(manager, source, factory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        var failure = await grain.Failed.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Equal("feature release unavailable", failure);
        Assert.Equal(0, factory.Created);
        Assert.False(grain.Committed.Task.IsCompleted);
    }

    [Fact]
    public async Task Ambiguous_commit_timeout_does_not_release_fence_as_failed()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        await using var manager = await ManagerAsync(release, new ImmediateGmailReader());
        var commitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var grain = new RecordingInstallationGrain(Claim(release.Descriptor.Digest), commitGate.Task);
        var worker = Worker(
            manager,
            new SingleWorkSource(new FeatureWorkItem(Installation, grain)),
            new SingleContextFactory(new RecordingRunContext()),
            new FeatureExecutionOptions(
                "feature-host-tests",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.Zero));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        await grain.CommitStarted.Task.WaitAsync(cancellation.Token);
        await Task.Delay(100, cancellation.Token);

        Assert.Equal(0, grain.Failures);
        commitGate.TrySetResult();
        cancellation.Cancel();
        await run;
    }

    [Fact]
    public async Task Insufficient_lease_budget_fails_before_context_creation()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        await using var manager = await ManagerAsync(release, new ImmediateGmailReader());
        var claim = Claim(release.Descriptor.Digest) with
        {
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(100)
        };
        var grain = new RecordingInstallationGrain(claim);
        var contexts = new CountingContextFactory();
        var worker = Worker(
            manager,
            new SingleWorkSource(new FeatureWorkItem(Installation, grain)),
            contexts);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        var failure = await grain.Failed.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Equal("feature lease budget insufficient", failure);
        Assert.Equal(0, contexts.Created);
    }

    [Fact]
    public async Task Cancellation_ignoring_handler_is_quarantined_without_retry()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var recycle = new RecordingRecycle();
        await using var manager = new FeatureReleaseManager(
            new SingleServiceProvider(new IgnoringGmailReader()),
            recycle);
        await manager.ActivateAsync(Installation, release.Descriptor);
        var grain = new RecordingInstallationGrain(Claim(release.Descriptor.Digest));
        var worker = Worker(
            manager,
            new SingleWorkSource(new FeatureWorkItem(Installation, grain)),
            new SingleContextFactory(new RecordingRunContext()),
            new FeatureExecutionOptions(
                "feature-host-tests",
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.Zero),
            recycle);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        await recycle.Requested.Task.WaitAsync(cancellation.Token);
        await run;

        Assert.Equal(0, grain.Failures);
        Assert.False(grain.Committed.Task.IsCompleted);
    }

    [Fact]
    public async Task Claim_timeout_recycles_the_host_without_starting_execution()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        await using var manager = await ManagerAsync(release, new ImmediateGmailReader());
        var pendingClaim = new TaskCompletionSource<FeatureRunClaim?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var grain = new RecordingInstallationGrain(
            Claim(release.Descriptor.Digest),
            claimCompletion: pendingClaim.Task);
        var contexts = new CountingContextFactory();
        var recycle = new RecordingRecycle();
        var worker = Worker(
            manager,
            new SingleWorkSource(new FeatureWorkItem(Installation, grain)),
            contexts,
            new FeatureExecutionOptions(
                "feature-host-tests",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.Zero),
            recycle);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.RunAsync(cancellation.Token);

        Assert.True(recycle.Requested.Task.IsCompleted);
        Assert.Equal(0, contexts.Created);
        Assert.Equal(0, grain.Failures);
    }

    [Fact]
    public async Task Handler_timeout_exception_is_a_normal_safe_failure()
    {
        using var release = FeatureReleaseTestArtifact.Create();
        var recycle = new RecordingRecycle();
        await using var manager = new FeatureReleaseManager(
            new SingleServiceProvider(new TimeoutGmailReader()),
            recycle);
        await manager.ActivateAsync(Installation, release.Descriptor);
        var grain = new RecordingInstallationGrain(Claim(release.Descriptor.Digest));
        var worker = Worker(
            manager,
            new SingleWorkSource(new FeatureWorkItem(Installation, grain)),
            new SingleContextFactory(new RecordingRunContext()),
            recycle: recycle);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = worker.RunAsync(cancellation.Token);
        var failure = await grain.Failed.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Equal("feature execution failed", failure);
        Assert.False(recycle.Requested.Task.IsCompleted);
    }

    private static FeatureExecutionWorker Worker(
        FeatureReleaseManager manager,
        IFeatureWorkSource source,
        IFeatureRunContextFactory contexts,
        FeatureExecutionOptions? options = null,
        IFeatureHostRecycle? recycle = null) =>
        new(
            manager,
            source,
            contexts,
            recycle ?? new NoopRecycle(),
            options ?? new FeatureExecutionOptions(
                "feature-host-tests",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1)),
            TimeProvider.System);

    private static async Task<FeatureReleaseManager> ManagerAsync(
        FeatureReleaseTestArtifact release,
        IGmailMessageReader reader)
    {
        var manager = new FeatureReleaseManager(new SingleServiceProvider(reader), new NoopRecycle());
        await manager.ActivateAsync(Installation, release.Descriptor);
        return manager;
    }

    private static FeatureRunClaim Claim(ReleaseDigest digest) => new(
        new GrainInput(
            "input-1",
            "gmail.message.summary.requested.v1",
            "{\"messageId\":\"message-1\"}",
            DateTimeOffset.UnixEpoch,
            "correlation-1",
            "trace-1"),
        new FeatureLeaseFence("input-1", 1),
        digest,
        "{}",
        DateTimeOffset.UtcNow.AddMinutes(1),
        1);

    private static bool RecordIntent(IFeatureContext context, int index)
    {
        try
        {
            context.Intents.AddTextSurface(new TextSurfaceIntent($"parallel-{index}", "title", "text"));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
    }

    private sealed class NoopRecycle : IFeatureHostRecycle
    {
        public void RequestRecycle()
        {
        }
    }

    private sealed class ImmediateGmailReader : IGmailMessageReader
    {
        public Task<GmailMessage> ReadAsync(
            GmailMessageReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GmailMessage(
                request.MessageId,
                null,
                DateTimeOffset.UnixEpoch,
                null,
                "subject",
                "body"));
    }

    private sealed class BlockingGmailReader : IGmailMessageReader
    {
        public bool Cancelled { get; private set; }

        public async Task<GmailMessage> ReadAsync(
            GmailMessageReadRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }

    private sealed class IgnoringGmailReader : IGmailMessageReader
    {
        private readonly TaskCompletionSource<GmailMessage> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GmailMessage> ReadAsync(
            GmailMessageReadRequest request,
            CancellationToken cancellationToken = default) =>
            _completion.Task;
    }

    private sealed class TimeoutGmailReader : IGmailMessageReader
    {
        public Task<GmailMessage> ReadAsync(
            GmailMessageReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GmailMessage>(new TimeoutException());
    }

    private sealed class RecordingRecycle : IFeatureHostRecycle
    {
        public TaskCompletionSource Requested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void RequestRecycle() => Requested.TrySetResult();
    }

    private sealed class SingleWorkSource(FeatureWorkItem work) : IFeatureWorkSource
    {
        private int _taken;

        public async ValueTask<FeatureWorkItem> TakeAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _taken, 1) == 0)
                return work;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class SingleContextFactory(RecordingRunContext context) : IFeatureRunContextFactory
    {
        public ValueTask<IFeatureRunContext> CreateAsync(
            FeatureWorkItem work,
            FeatureRunClaim claim,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IFeatureRunContext>(context);
    }

    private sealed class CountingContextFactory : IFeatureRunContextFactory
    {
        public int Created { get; private set; }

        public ValueTask<IFeatureRunContext> CreateAsync(
            FeatureWorkItem work,
            FeatureRunClaim claim,
            CancellationToken cancellationToken = default)
        {
            Created++;
            return ValueTask.FromResult<IFeatureRunContext>(new RecordingRunContext());
        }
    }

    private sealed class RecordingRunContext : IFeatureRunContext, IFeatureContext
    {
        private readonly State _state = new();
        private readonly IntentBuffer _intents = new();
        private readonly ModelWorkflow _models = new();

        public IFeatureContext Context => this;
        public IFeatureClock Clock { get; } = new Clock();
        public IFeatureIdentifiers Identifiers { get; } = new Identifiers();
        public IFeatureState State => _state;
        public IMemoryRecall MemoryRecall { get; } = new MemoryRecall();
        public IMemoryRemember MemoryRemember { get; } = new MemoryRemember();
        public IModelWorkflow Models => _models;
        public IFeatureIntentBuffer Intents => _intents;
        public ModelResponse ModelResponse => _models.Response;
        public bool Disposed { get; private set; }

        public ValueTask<FeatureRunCommit> SealAsync(
            FeatureLeaseFence fence,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FeatureRunCommit(
                fence,
                _state.Read().Json,
                _intents.Items,
                new FeatureResourceUsage(0, _models.Calls),
                "{}"));
        }

        public IDisposable Activate() => NoopDisposable.Instance;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose()
        {
        }
    }

    private sealed class FeatureCapabilityClient : IFeatureCapabilityClient
    {
        public Task<JsonElement> ExecuteAsync(
            CapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = request.CapabilityId switch
            {
                GoogleCapabilityIds.GmailMessageRead => JsonSerializer.SerializeToElement(new GmailMessage(
                    "message-1",
                    "thread-1",
                    DateTimeOffset.UnixEpoch,
                    "sender@example.com",
                    "Subject",
                    "Body")),
                "model.complete.v1" => JsonSerializer.SerializeToElement(new { text = "summary" }),
                _ => throw new InvalidOperationException(request.CapabilityId)
            };
            return Task.FromResult(payload);
        }
    }

    private sealed class CountingCapabilityClient : IFeatureCapabilityClient
    {
        private int _reads;
        private int _models;
        public int Reads => Volatile.Read(ref _reads);
        public int Models => Volatile.Read(ref _models);

        public Task<JsonElement> ExecuteAsync(
            CapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(request.CapabilityId, "memory.recall", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _reads);
                return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>() }));
            }
            if (string.Equals(request.CapabilityId, "model.complete.v1", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _models);
                return Task.FromResult(JsonSerializer.SerializeToElement(new { text = "response" }));
            }
            throw new InvalidOperationException(request.CapabilityId);
        }
    }

    private sealed class BlockingCapabilityClient : IFeatureCapabilityClient
    {
        private int _calls;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);
        public bool Cancelled { get; private set; }

        public async Task<JsonElement> ExecuteAsync(
            CapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }

    private sealed class Clock : IFeatureClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class Identifiers : IFeatureIdentifiers
    {
        private int _next;
        public string Next(string scope) => $"{scope}-{Interlocked.Increment(ref _next)}";
    }

    private sealed class State : IFeatureState
    {
        private FeatureState _state = new("{}");
        public FeatureState Read() => _state;
        public void Replace(FeatureState state) => _state = state;
    }

    private sealed class MemoryRecall : IMemoryRecall
    {
        public Task<IReadOnlyList<MemoryFact>> RecallAsync(
            MemoryRecallRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryFact>>([]);
    }

    private sealed class MemoryRemember : IMemoryRemember
    {
        public void Remember(MemoryRememberIntent intent)
        {
        }
    }

    private sealed class ModelWorkflow : IModelWorkflow
    {
        public ModelResponse Response { get; } = new("summary");
        public int Calls { get; private set; }

        public Task<ModelResponse> CompleteAsync(
            ModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Response);
        }
    }

    private sealed class IntentBuffer : IFeatureIntentBuffer
    {
        public List<FeatureIntent> Items { get; } = [];

        public void AddTextSurface(TextSurfaceIntent intent) => Items.Add(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.TextSurface,
            JsonSerializer.Serialize(new { intent.Title, intent.Text })));

        public void EmitEvent(EventIntent intent) => Items.Add(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.Event,
            intent.Json));

        public void ProposeExternalEffect(ExternalEffectIntent intent) => Items.Add(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.ExternalEffect,
            intent.Json));
    }

    private sealed class RecordingInstallationGrain(
        FeatureRunClaim claim,
        Task? commitCompletion = null,
        Task<FeatureRunClaim?>? claimCompletion = null) : IFeatureInstallationGrain
    {
        private int _claimed;
        public TaskCompletionSource<FeatureRunCommit> Committed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CommitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Failures { get; private set; }

        public Task<FeatureRunClaim?> ClaimAsync(string hostId, TimeSpan leaseDuration) =>
            claimCompletion ?? Task.FromResult(Interlocked.Exchange(ref _claimed, 1) == 0 ? claim : null);

        public async Task<FeatureCompletionReceipt> CommitAsync(FeatureRunCommit commit)
        {
            CommitStarted.TrySetResult();
            if (commitCompletion is not null)
                await commitCompletion;
            Committed.TrySetResult(commit);
            return new FeatureCompletionReceipt(
                commit.Fence.InputId,
                commit.Fence.Fence,
                commit.ResultJson,
                DateTimeOffset.UnixEpoch,
                new string('a', 64),
                new string('b', 64));
        }

        public Task<FeatureFailureDisposition> FailAsync(
            FeatureLeaseFence fence,
            DateTimeOffset retryAt,
            string safeFailure)
        {
            Failures++;
            Failed.TrySetResult(safeFailure);
            return Task.FromResult(FeatureFailureDisposition.RetryScheduled);
        }

        public Task InitializeAsync(ReleaseDigest release) => throw new NotSupportedException();
        public Task<FeatureAppendStatus> AppendAsync(GrainInput input) => throw new NotSupportedException();
        public Task<FeatureAppendStatus> RecordScheduleOccurrenceAsync(FeatureScheduleOccurrence occurrence) => throw new NotSupportedException();
        public Task<FeatureIntentStatus[]> ListPendingIntentsAsync() => throw new NotSupportedException();
        public Task ApplyIntentAsync(string operationKey) => throw new NotSupportedException();
        public Task PauseAsync(string reason) => throw new NotSupportedException();
        public Task ResumeAsync() => throw new NotSupportedException();
        public Task SwitchReleaseAsync(ReleaseDigest release) => throw new NotSupportedException();
        public Task RollbackAsync() => throw new NotSupportedException();
        public Task<FeatureInstallationSnapshot> ReadAsync() => throw new NotSupportedException();
    }
}
