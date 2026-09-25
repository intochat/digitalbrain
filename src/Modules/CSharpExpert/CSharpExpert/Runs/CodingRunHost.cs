using System.Collections.Concurrent;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.CSharpExpert;

public sealed class CodingRunHost(IDigitalBrain brain, ILogger<CodingRunHost> log)
{
    private readonly ConcurrentDictionary<string, CodingRunSession> sessions = new();

    public async Task<CodingRunSnapshot> StartAsync(FeatureRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runId = "run-" + Guid.NewGuid().ToString("N");
        var run = brain.Get<ICodingRun>(runId);
        var session = new CodingRunSession(brain, runId, log);
        if (!sessions.TryAdd(runId, session))
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("This run is already active.");
        }

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
}

internal sealed class CodingRunSession
{
    private static readonly Type[] RequiredSignals = [typeof(FeatureRequested), typeof(ContextReady), typeof(PlanClarified)];

    private readonly CancellationTokenSource cancel = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task loops;

    public CodingRunSession(IDigitalBrain brain, string runId, ILogger log)
    {
        loops = RunAsync(new ObservedBrain(brain, RequiredSignals, ready), runId, log);
    }

    public Task WhenReadyAsync(CancellationToken cancellation) => ready.Task.WaitAsync(cancellation);

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
            await Task.WhenAll(
                new UnderstandSolution(observed, runId).RunAsync(cancel.Token),
                new PlanFeature(observed, runId).RunAsync(cancel.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Coding run {RunId} behaviors stopped.", runId);
        }
    }

    private sealed class ObservedBrain(IDigitalBrain inner, IReadOnlyCollection<Type> expected, TaskCompletionSource ready) : IDigitalBrain
    {
        private readonly ConcurrentDictionary<Type, byte> seen = new();

        public T Get<T>(string id) where T : class, IGrainWithStringKey => inner.Get<T>(id);

        public async Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        {
            var subscription = await inner.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
            seen.TryAdd(typeof(T), 0);
            if (expected.All(seen.ContainsKey))
            {
                ready.TrySetResult();
            }

            return subscription;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
