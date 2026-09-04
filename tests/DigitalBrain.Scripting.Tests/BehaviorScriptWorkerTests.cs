using DigitalBrain.Scripting.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class BehaviorScriptWorkerTests
{
    [Fact]
    public async Task Duplicate_admission_of_the_same_source_runs_once()
    {
        var admitted = new AdmittedBehavior("elon-chart", "return 1;");
        var source = new TestAdmissionSource(admitted, admitted);
        var runner = new CountingRunner(new StartupScriptRunResult(true, "started", []));
        var worker = new BehaviorScriptWorker(
            source,
            runner,
            new FakeDigitalBrain("alice"),
            NullLogger<BehaviorScriptWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await source.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => runner.InvocationCount == 1, TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, runner.InvocationCount);
    }

    [Fact]
    public async Task A_changed_source_replaces_the_running_behavior()
    {
        var source = new TestAdmissionSource(
            new AdmittedBehavior("elon-chart", "return 1;"),
            new AdmittedBehavior("elon-chart", "return 2;"));
        var runner = new CountingRunner(new StartupScriptRunResult(true, "started", []));
        var worker = new BehaviorScriptWorker(
            source,
            runner,
            new FakeDigitalBrain("alice"),
            NullLogger<BehaviorScriptWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await source.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => runner.InvocationCount == 2, TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.InvocationCount);
    }

    [Fact]
    public async Task Cancellation_stops_a_running_behavior()
    {
        var source = new TestAdmissionSource(new AdmittedBehavior("elon-chart", "await Task.Delay(-1);"));
        var runner = new CancellingRunner();
        var worker = new BehaviorScriptWorker(
            source,
            runner,
            new FakeDigitalBrain("alice"),
            NullLogger<BehaviorScriptWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await runner.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        await runner.Cancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class TestAdmissionSource(params AdmittedBehavior[] admissions) : IBehaviorAdmissionSource
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IAsyncEnumerable<AdmittedBehavior> WatchAsync(CancellationToken cancellationToken)
            => Watch(cancellationToken);

        private async IAsyncEnumerable<AdmittedBehavior> Watch(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            try
            {
                foreach (var admitted in admissions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return admitted;
                }
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class CountingRunner(StartupScriptRunResult result) : IStartupScriptRunner
    {
        public int InvocationCount { get; private set; }

        public Task<StartupScriptRunResult> RunAsync(
            StartupScript script,
            DigitalBrain.Abstractions.IDigitalBrain brain,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class CancellingRunner : IStartupScriptRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StartupScriptRunResult> RunAsync(
            StartupScript script,
            DigitalBrain.Abstractions.IDigitalBrain brain,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("Cancellation did not stop the runner.");
        }
    }
}
