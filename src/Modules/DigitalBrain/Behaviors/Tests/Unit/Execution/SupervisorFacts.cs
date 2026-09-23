using System.Collections.Concurrent;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SupervisorFacts
{
    [Fact]
    public async Task RepeatedCrashesExhaustTheRetryBudget()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        var executor = new ControlledExecutor { CrashImmediately = true };
        try
        {
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(executor);
                s.Services.AddSingleton<ICodeArtifactStore>(new TestArtifacts());
            }).StartAsync(ct);
            var program = brain.Get<IBehaviorProgram>("crash-loop");
            await program.Deploy(new(0, Guid.NewGuid(), new("one", "source", "env"), "{}"), ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (executor.StartCount < 4) { await Task.Delay(50, deadline.Token); }
            await WaitFor(program, BehaviorExecutionState.Failed, ct);
            await Task.Delay(500, ct);
            Assert.Equal(4, executor.StartCount);
            Assert.False((await program.Read(ct)).Ready);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task UnconfiguredModuleDoesNotRequireExecutionStorageOrArtifactServices()
    {
        await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            s.Services.Configure<BehaviorOptions>(o => o.Root = "")).StartAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IBehaviorProgram>("disabled").Read(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StopWhileLaunchIsPendingPreventsLateReadiness()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        var executor = new ControlledExecutor { PauseStart = true };
        try
        {
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(executor);
                s.Services.AddSingleton<ICodeArtifactStore>(new TestArtifacts());
            }).StartAsync(ct);
            var program = brain.Get<IBehaviorProgram>("pending-stop");
            var deployment = await program.Deploy(new(0, Guid.NewGuid(), new("one", "source", "env"), "{}"), ct);
            var launch = await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await program.Stop(new(deployment.Revision, Guid.NewGuid()), ct);
            await launch.Readiness(true);
            Assert.False((await program.Read(ct)).Ready);
            executor.ReleaseStart.TrySetResult();
            await WaitFor(program, BehaviorExecutionState.Stopped, ct);
            Assert.Equal(0, executor.ActiveCount);
            Assert.Equal(1, executor.StartCount);
        }
        finally { executor.ReleaseStart.TrySetResult(); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task FailedStopNeverStartsAReplacement()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        var executor = new ControlledExecutor { FailStop = true };
        try
        {
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(executor);
                s.Services.AddSingleton<ICodeArtifactStore>(new TestArtifacts());
            }).StartAsync(ct);
            var program = brain.Get<IBehaviorProgram>("failed-stop");
            var first = await program.Deploy(new(0, Guid.NewGuid(), new("one", "source", "env"), "{}"), ct);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await program.Deploy(new(first.Revision, Guid.NewGuid(), new("two", "source", "env"), "{}"), ct);
            await executor.StopAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await Task.Delay(250, ct);
            Assert.Equal(1, executor.StartCount);
            Assert.Equal(1, executor.ActiveCount);
            executor.FailStop = false;
        }
        finally { executor.FailStop = false; if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task CommittedDeploymentReplaysWithoutReverifyingMissingArtifact()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        var artifacts = new TestArtifacts();
        try
        {
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(new ControlledExecutor());
                s.Services.AddSingleton<ICodeArtifactStore>(artifacts);
            }).StartAsync(ct);
            var program = brain.Get<IBehaviorProgram>("replay");
            var request = new DeployBehavior(0, Guid.NewGuid(), new("id", "source", "env"), "{}");
            var first = await program.Deploy(request, ct);
            artifacts.Unavailable = true;
            var replay = await program.Deploy(request, ct);
            Assert.Equal(first.Revision, replay.Revision);
            Assert.Equal(first.Deployments.Single().Artifact, replay.Deployments.Single().Artifact);
            await Assert.ThrowsAsync<InvalidOperationException>(() => program.Deploy(request with { ConfigurationJson = "{\"Behavior__X\":\"y\"}" }, ct));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task RecoveryRestartsRunningIntentButPreservesStoppedIntent()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BehaviorProgramStore(Path.Combine(root, "programs"));
            await store.DeployAsync("running", new(0, Guid.NewGuid(), new("id", "source", "env"), "{}"), ct);
            var stopped = await store.DeployAsync("stopped", new(0, Guid.NewGuid(), new("id", "source", "env"), "{}"), ct);
            await store.ChangeAsync("stopped", new(stopped.Revision, Guid.NewGuid()), false, ct);
            var old = Guid.NewGuid();
            await store.UpdateAsync("running", d => d.Snapshot = d.Snapshot with { State = BehaviorExecutionState.Running, GenerationId = old, Ready = true }, ct);
            var executor = new ControlledExecutor();
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(executor);
                s.Services.AddSingleton<ICodeArtifactStore>(new TestArtifacts());
            }).StartAsync(ct);
            var launch = await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Equal("running", launch.ProgramId);
            Assert.NotEqual(old, launch.GenerationId);
            await WaitFor(brain.Get<IBehaviorProgram>("stopped"), BehaviorExecutionState.Stopped, ct);
            Assert.Equal(1, executor.StartCount);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task StopAndLateReadyDoNotReviveTheOldGeneration()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        var executor = new ControlledExecutor();
        try
        {
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(executor);
                s.Services.AddSingleton<ICodeArtifactStore>(new TestArtifacts());
            }).StartAsync(ct);
            var program = brain.Get<IBehaviorProgram>("workspace/program");
            var deployed = await program.Deploy(new(0, Guid.NewGuid(), new("id", "source", "env"), "{}"), ct);
            var first = await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await first.Readiness(true);
            Assert.True((await program.Read(ct)).Ready);
            await brain.DeactivateAsync(program, ct);
            Assert.True((await program.Read(ct)).Ready);
            await program.Stop(new(deployed.Revision, Guid.NewGuid()), ct);
            await first.Readiness(true);
            await WaitFor(program, BehaviorExecutionState.Stopped, ct);
            Assert.False((await program.Read(ct)).Ready);
            Assert.Equal(0, executor.ActiveCount);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task ReplacementStopsOldWorkerBeforeStartingNewWorker()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "brain-supervisor-tests", Guid.NewGuid().ToString("N"));
        var executor = new ControlledExecutor();
        try
        {
            await using var brain = await UnitTest.Create().WithModule<BehaviorModule>().ConfigureSilo(s =>
            {
                s.Services.Configure<BehaviorOptions>(o => o.Root = root);
                s.Services.AddSingleton<IBehaviorExecutor>(executor);
                s.Services.AddSingleton<ICodeArtifactStore>(new TestArtifacts());
            }).StartAsync(ct);
            var program = brain.Get<IBehaviorProgram>("p");
            var first = await program.Deploy(new(0, Guid.NewGuid(), new("one", "source", "env"), "{}"), ct);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await program.Deploy(new(first.Revision, Guid.NewGuid(), new("two", "source", "env"), "{}"), ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (executor.StartCount < 2) { await Task.Delay(20, deadline.Token); }
            Assert.Equal(1, executor.MaximumActive);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    private static async Task WaitFor(IBehaviorProgram program, BehaviorExecutionState state, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while ((await program.Read(deadline.Token)).State != state) { await Task.Delay(20, deadline.Token); }
    }

    private sealed class ControlledExecutor : IBehaviorExecutor
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<BehaviorExit>> _runs = new();
        private int _starts;
        public TaskCompletionSource<BehaviorLaunch> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActiveCount => _runs.Count;
        public int StartCount => Volatile.Read(ref _starts);
        public int MaximumActive { get; private set; }
        public bool FailStop { get; set; }
        public bool PauseStart { get; init; }
        public bool CrashImmediately { get; init; }
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BehaviorExecution> StartAsync(BehaviorLaunch launch, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<BehaviorExit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _runs[launch.GenerationId] = completion;
            MaximumActive = Math.Max(MaximumActive, _runs.Count);
            Interlocked.Increment(ref _starts);
            Started.TrySetResult(launch);
            if (CrashImmediately) { _runs.TryRemove(launch.GenerationId, out _); completion.TrySetResult(new(1, "crash")); }
            if (PauseStart) { await ReleaseStart.Task.WaitAsync(cancellationToken); }
            return new BehaviorExecution(launch.GenerationId, 1, completion.Task);
        }
        public Task StopAsync(Guid generationId, CancellationToken cancellationToken)
        {
            StopAttempted.TrySetResult();
            if (FailStop) { throw new TimeoutException("Worker exit not confirmed."); }
            if (_runs.TryRemove(generationId, out var run)) { run.TrySetResult(new(0, null)); }
            return Task.CompletedTask;
        }
    }

    private sealed class TestArtifacts : ICodeArtifactStore
    {
        public bool Unavailable { get; set; }
        public Task<VerifiedArtifact> OpenVerifiedAsync(CodeArtifactRef reference, CancellationToken cancellationToken)
            => Unavailable ? Task.FromException<VerifiedArtifact>(new FileNotFoundException("Artifact unavailable")) : Task.FromResult(new VerifiedArtifact(reference, new("source", "tests", new("sdk", "os", "template", "validator", new Dictionary<string, string>()),
                "app.dll", new(1, 1, 0, 0, "report"), new Dictionary<string, string>()), Path.GetTempPath(), "app.dll"));
        public Task<CodeArtifactRef> SealAsync(VerifiedBuild build, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}