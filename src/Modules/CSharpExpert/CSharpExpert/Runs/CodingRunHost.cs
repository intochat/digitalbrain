using System.Collections.Concurrent;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.CSharpExpert;

public sealed class CodingRunHost(IDigitalBrain brain, WorkspacePreparer preparer, SolutionPolicy policy, ILogger<CodingRunHost> log) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CodingRunSession> sessions = new();

    public bool IsActive(string runId) => sessions.ContainsKey(runId);

    public async Task<CodingRunSnapshot> StartAsync(FeatureRequest request, CancellationToken cancellation, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = request with { SolutionPath = policy.Validate(request.SolutionPath) };
        runId ??= "run-" + Guid.NewGuid().ToString("N");
        var run = brain.Get<ICodingRun>(runId);
        var session = new CodingRunSession(brain, runId, preparer, log);
        if (!sessions.TryAdd(runId, session))
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("This run is already active.");
        }

        _ = CompleteOnTerminalAsync(runId, session);
        try
        {
            await session.WhenReadyAsync(cancellation).ConfigureAwait(false);
            await run.Request(request).WaitAsync(cancellation).ConfigureAwait(false);
            return await run.Read().WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch
        {
            sessions.TryRemove(runId, out _);
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(string runId, CancellationToken cancellation)
    {
        if (sessions.TryRemove(runId, out var session))
        {
            await session.StopAsync(cancellation).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var active = sessions.Values.ToArray();
        sessions.Clear();
        foreach (var session in active)
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // A run that reaches Finished, Failed, Stopped or NeedsHuman keeps no session: the behaviors are
    // cancelled and the entry is dropped so a later run with the same id starts clean.
    private async Task CompleteOnTerminalAsync(string runId, CodingRunSession session)
    {
        await session.Completion.ConfigureAwait(false);
        if (sessions.TryRemove(runId, out var removed) && ReferenceEquals(removed, session))
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var snapshot = await brain.Get<ICodingRun>(runId).Read().ConfigureAwait(false);
        if (snapshot.Status is CodingRunStatus.Failed or CodingRunStatus.Stopped && snapshot.WorkspaceRoot is { } workspace)
        {
            try
            {
                await preparer.ReleaseAsync(workspace, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                log.LogWarning(error, "Could not remove the workspace {Workspace} of run {RunId}.", workspace, runId);
            }
        }
    }
}

internal sealed class CodingRunSession
{
    private static readonly Type[] TerminalSignals = [typeof(RunFinished), typeof(RunFailed), typeof(RunStopped), typeof(NeedsHuman)];

    private readonly CancellationTokenSource cancel = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IReadOnlyList<IBehavior> behaviors;
    private readonly Task loops;

    public CodingRunSession(IDigitalBrain brain, string runId, WorkspacePreparer preparer, ILogger log)
    {
        var observed = new ObservedBrain(brain, ready);
        behaviors =
        [
            new UnderstandSolution(observed, runId),
            new PlanFeature(observed, runId),
            new PrepareWorkspace(observed, runId, preparer),
            new ImplementStep(observed, runId),
            new BuildSolution(observed, runId),
            new RunTests(observed, runId),
            new FixStep(observed, runId),
            new ReviewStep(observed, runId),
        ];
        observed.Expect(behaviors.OfType<IBehaviorSignals>()
            .SelectMany(behavior => behavior.Signals)
            .Concat(TerminalSignals));
        loops = RunAsync(observed, runId, log);
    }

    public Task Completion => terminal.Task;

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    public Task WhenReadyAsync(CancellationToken cancellation) => ready.Task.WaitAsync(ReadyTimeout, cancellation);

    public async Task StopAsync(CancellationToken cancellation)
    {
        await cancel.CancelAsync().ConfigureAwait(false);
        try
        {
            await loops.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancel.Dispose();
        }
    }

    private async Task RunAsync(IDigitalBrain observed, string runId, ILogger log)
    {
        try
        {
            await Task.WhenAll(behaviors.Select(behavior => behavior.RunAsync(cancel.Token))
                .Append(WatchTerminalAsync(observed, runId, cancel.Token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Coding run {RunId} behaviors stopped.", runId);
        }
        finally
        {
            ready.TrySetException(new InvalidOperationException("The run's behaviors stopped before they were listening."));
        }
    }

    private async Task WatchTerminalAsync(IDigitalBrain observed, string runId, CancellationToken cancellation)
    {
        var run = observed.Get<ICodingRun>(runId);
        await Task.WhenAll(
            WatchAsync<RunFinished>(observed, run, cancellation),
            WatchAsync<RunFailed>(observed, run, cancellation),
            WatchAsync<RunStopped>(observed, run, cancellation),
            WatchAsync<NeedsHuman>(observed, run, cancellation)).ConfigureAwait(false);
    }

    private async Task WatchAsync<TSignal>(IDigitalBrain observed, ICodingRun run, CancellationToken cancellation) where TSignal : Signal
    {
        await foreach (var _ in observed.On<TSignal>(run, cancellation).ConfigureAwait(false))
        {
            terminal.TrySetResult();
        }
    }

    private sealed class ObservedBrain(IDigitalBrain inner, TaskCompletionSource ready) : IDigitalBrain
    {
        private readonly ConcurrentDictionary<Type, byte> seen = new();
        private Type[] expected = [];

        public void Expect(IEnumerable<Type> signals) => expected = signals.Distinct().ToArray();

        public T Get<T>(string id) where T : class, IGrainWithStringKey => inner.Get<T>(id);

        public async Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        {
            var subscription = await inner.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
            seen.TryAdd(typeof(T), 0);
            if (expected.Length > 0 && expected.All(seen.ContainsKey))
            {
                ready.TrySetResult();
            }

            return subscription;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
