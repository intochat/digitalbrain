using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class BehaviorScriptWorkerTests
{
    [Fact]
    public async Task Repeated_snapshots_do_not_execute_the_same_revision_twice()
    {
        var definition = Definition("review", "return 1;");
        var source = new TestAdmissionSource();
        var runner = new CountingRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(definition);
        await source.WaitForAsync(report => report.Status == BehaviorStatus.Completed);
        source.Publish(definition);
        source.Publish(definition);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, runner.InvocationCount);
    }

    [Fact]
    public async Task Explicit_readmission_with_identical_source_runs_the_new_revision()
    {
        var first = Definition("review", "return 1;");
        var second = first with { Revision = Guid.NewGuid() };
        var source = new TestAdmissionSource();
        var runner = new CountingRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(first);
        await source.WaitForAsync(report => report.Revision == first.Revision && report.Status == BehaviorStatus.Completed);
        source.Publish(second);
        await source.WaitForAsync(report => report.Revision == second.Revision && report.Status == BehaviorStatus.Completed);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, runner.InvocationCount);
    }

    [Fact]
    public async Task Removed_definition_cancels_its_running_script()
    {
        var source = new TestAdmissionSource();
        var runner = new CancellingRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(Definition("review", "await Task.Delay(-1, CancellationToken);"));
        await runner.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        source.Publish();
        await runner.Cancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(source.Reports, report => report.Status == BehaviorStatus.Completed);
    }

    [Fact]
    public async Task Restart_resumes_running_definitions_but_not_completed_or_failed_ones()
    {
        var source = new TestAdmissionSource();
        var runner = new CountingRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(
            Definition("complete", "return 1;") with { Status = BehaviorStatus.Completed },
            Definition("failed", "return 1;") with { Status = BehaviorStatus.Failed },
            Definition("running", "return 1;") with { Status = BehaviorStatus.Running });
        await source.WaitForAsync(report => report.Name == "running" && report.Status == BehaviorStatus.Completed);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, runner.InvocationCount);
    }

    [Fact]
    public async Task Compilation_diagnostics_are_reported_for_the_executing_revision()
    {
        var definition = Definition("broken", "this is not C#;");
        var source = new TestAdmissionSource();
        using var worker = Worker(source, new CSharpStartupScriptRunner());
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(definition);
        var report = await source.WaitForAsync(report => report.Status == BehaviorStatus.Failed);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(definition.Revision, report.Revision);
        Assert.Equal("Compilation failed.", report.Summary);
        Assert.NotEmpty(report.Diagnostics);
    }

    [Fact]
    public async Task Script_and_its_reports_run_as_the_admitting_principal()
    {
        var principal = PrincipalId.New();
        var source = new TestAdmissionSource();
        var runner = new CountingRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(Definition("review", "return 1;") with { Principal = principal });
        await source.WaitForAsync(report => report.Status == BehaviorStatus.Completed);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(principal, runner.Principal);
        Assert.All(source.ReportPrincipals, actual => Assert.Equal(principal, actual));
        Assert.Null(VerifiedActor.Current);
    }

    [Fact]
    public async Task Replacement_is_serialized_even_when_a_script_cancellation_callback_throws()
    {
        var source = new TestAdmissionSource();
        var runner = new ThrowingCancellationRunner();
        using var worker = Worker(source, runner);
        var first = Definition("review", "first");
        var replacement = Definition("review", "second");
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(first);
        await runner.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        source.Publish(replacement);
        await source.WaitForAsync(report => report.Revision == replacement.Revision && report.Status == BehaviorStatus.Completed);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(runner.PreviousStoppedBeforeReplacement);
        Assert.False(worker.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Throwing_cancellation_callback_cannot_escape_host_shutdown()
    {
        var source = new TestAdmissionSource();
        var runner = new ThrowingCancellationRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(Definition("review", "first"));
        await runner.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(worker.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Shutdown_has_a_bound_when_a_script_ignores_cancellation()
    {
        var source = new TestAdmissionSource();
        var runner = new IgnoringCancellationRunner();
        using var worker = Worker(source, runner);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        source.Publish(Definition("stubborn", "await SomethingWithoutCancellation();"));
        await runner.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            await worker.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            Assert.DoesNotContain(source.Reports, report => report.Status == BehaviorStatus.Completed);
        }
        finally
        {
            runner.Release.TrySetResult();
        }
    }

    private static BehaviorDefinition Definition(string name, string source)
        => new(name, source, Guid.NewGuid(), BehaviorStatus.Admitted, "", []);

    private static BehaviorScriptWorker Worker(IBehaviorAdmissionSource source, IStartupScriptRunner runner)
        => new(source, runner, new FakeDigitalBrain("alice"), NullLogger<BehaviorScriptWorker>.Instance);

    private sealed class TestAdmissionSource : IBehaviorAdmissionSource
    {
        private readonly Channel<IReadOnlyList<BehaviorDefinition>> _snapshots
            = Channel.CreateUnbounded<IReadOnlyList<BehaviorDefinition>>();

        public ConcurrentQueue<ReportBehaviorStatus> Reports { get; } = new();
        public ConcurrentQueue<PrincipalId?> ReportPrincipals { get; } = new();

        public void Publish(params BehaviorDefinition[] definitions) => _snapshots.Writer.TryWrite(definitions);

        public IAsyncEnumerable<IReadOnlyList<BehaviorDefinition>> WatchAsync(CancellationToken cancellationToken)
            => _snapshots.Reader.ReadAllAsync(cancellationToken);

        public Task ReportAsync(ReportBehaviorStatus report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportPrincipals.Enqueue(VerifiedActor.Current?.PrincipalId);
            Reports.Enqueue(report);
            return Task.CompletedTask;
        }

        public async Task<ReportBehaviorStatus> WaitForAsync(Func<ReportBehaviorStatus, bool> predicate)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                if (Reports.FirstOrDefault(predicate) is { } report)
                {
                    return report;
                }
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    private sealed class CountingRunner : IStartupScriptRunner
    {
        private int _invocationCount;
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public PrincipalId? Principal { get; private set; }

        public Task<StartupScriptRunResult> RunAsync(StartupScript script, IDigitalBrain brain, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            Principal = VerifiedActor.Current?.PrincipalId;
            return Task.FromResult(new StartupScriptRunResult(true, "done", []));
        }
    }

    private sealed class CancellingRunner : IStartupScriptRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StartupScriptRunResult> RunAsync(StartupScript script, IDigitalBrain brain, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                Cancelled.TrySetResult();
            }
            return new StartupScriptRunResult(true, "done", []);
        }
    }

    private sealed class IgnoringCancellationRunner : IStartupScriptRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StartupScriptRunResult> RunAsync(StartupScript script, IDigitalBrain brain, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            return new StartupScriptRunResult(true, "done", []);
        }
    }

    private sealed class ThrowingCancellationRunner : IStartupScriptRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _firstStopped;
        public bool PreviousStoppedBeforeReplacement { get; private set; }

        public async Task<StartupScriptRunResult> RunAsync(StartupScript script, IDigitalBrain brain, CancellationToken cancellationToken)
        {
            if (script.Source == "second")
            {
                PreviousStoppedBeforeReplacement = _firstStopped;
                return new StartupScriptRunResult(true, "done", []);
            }
            using var registration = cancellationToken.Register(() => throw new InvalidOperationException("bad callback"));
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                _firstStopped = true;
            }
            return new StartupScriptRunResult(true, "done", []);
        }
    }
}
