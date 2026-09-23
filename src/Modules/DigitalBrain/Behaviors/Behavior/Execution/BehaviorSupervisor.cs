using DigitalBrain.Coding;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace DigitalBrain.Behavior;

internal sealed class BehaviorSupervisor(IOptions<BehaviorOptions> options, IBehaviorExecutor executor,
    IServiceProvider services, IGrainFactory grains, ILogger<BehaviorSupervisor> logger) : BackgroundService
{
    private readonly Dictionary<string, BehaviorExecution> _active = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private FileStream? _ownership;
    private BehaviorProgramStore? _store;
    private BehaviorLogStore? _logs;
    private CancellationToken _stopping;
    public BehaviorProgramStore Store => _store ?? throw new InvalidOperationException("Behavior runtime is not configured.");
    public BehaviorLogStore Logs => _logs ?? throw new InvalidOperationException("Behavior runtime is not configured.");
    private ICodeArtifactStore Artifacts => (ICodeArtifactStore)(services.GetService(typeof(ICodeArtifactStore)) ?? throw new InvalidOperationException("Code artifact store is not configured."));

    public void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Root is { Length: > 0 } root && !string.IsNullOrWhiteSpace(root))
        {
            Directory.CreateDirectory(root);
            _ownership = new(Path.Combine(root, "supervisor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _store = new((services.GetService(typeof(IDocumentStore<BehaviorProgramDocument>)) as IDocumentStore<BehaviorProgramDocument>) ?? new GrainDocumentStore<BehaviorProgramDocument>(grains, "behavior-programs"));
            _logs = new((services.GetService(typeof(IDocumentStore<LogDocument>)) as IDocumentStore<LogDocument>) ?? new GrainDocumentStore<LogDocument>(grains, "behavior-logs"), options.Value.MaximumLogBytes);
            foreach (var id in await Store.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                await Store.UpdateAsync(id, d =>
                {
                    if (d.Snapshot.State is BehaviorExecutionState.Starting or BehaviorExecutionState.Running or BehaviorExecutionState.Stopping)
                    { d.Snapshot = d.Snapshot with { State = BehaviorExecutionState.Interrupted, Ready = false, GenerationId = null, ActiveDeploymentRevision = null }; }
                    return true;
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_store is null) { return; }
        _stopping = stoppingToken;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Drain before scanning so a wake that lands mid-scan keeps its own rescan instead of
                // being coalesced into the scan that may not have observed its write yet.
                while (_wake.Wait(0)) { }
                foreach (var id in await Store.ListAsync(stoppingToken).ConfigureAwait(false))
                {
                    try { await Reconcile(id, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception error) { logger.LogError(error, "Behavior reconciliation failed for {Program}", id); }
                }
                await _wake.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var worker in _active.Values)
            {
                try { await executor.StopAsync(worker.GenerationId, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { logger.LogError(error, "Cannot stop behavior generation {Generation}", worker.GenerationId); }
            }
            _active.Clear();
        }
    }

    // Timers can fire a few milliseconds before the due time, and the retry gate compares against the
    // clock, so the wake lands just after the retry is due rather than exactly on it.
    private static readonly TimeSpan RetryWakeMargin = TimeSpan.FromMilliseconds(100);

    private void ScheduleRetryWake(DateTimeOffset when)
    {
        var delay = when - DateTimeOffset.UtcNow + RetryWakeMargin;
        _ = Task.Delay(delay, _stopping).ContinueWith(_ => Wake(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ObserveCompletion(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch { }
        Wake();
    }

    private async Task Reconcile(string id, CancellationToken ct)
    {
        var state = await Store.ReadAsync(id, ct).ConfigureAwait(false);
        if (_active.TryGetValue(id, out var active))
        {
            if (state.GenerationId != active.GenerationId || state.DesiredState == BehaviorDesiredState.Stopped)
            {
                await executor.StopAsync(active.GenerationId, ct).ConfigureAwait(false);
                await active.Completion.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                _active.Remove(id);
            }
            else if (active.Completion.IsCompleted)
            {
                var exit = await active.Completion.ConfigureAwait(false);
                _active.Remove(id);
                var retryAt = await Store.UpdateAsync(id, d =>
                {
                    if (d.Snapshot.GenerationId != active.GenerationId) { return null; }
                    d.Retries++;
                    d.NextRetryAt = exit.ExitCode == 0 && exit.Error is null ? null : DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, d.Retries - 1));
                    d.Snapshot = d.Snapshot with
                    {
                        State = exit.ExitCode == 0 && exit.Error is null ? BehaviorExecutionState.Completed : BehaviorExecutionState.Failed,
                        Ready = false,
                        Error = exit.Error ?? (exit.ExitCode == 0 ? null : $"Worker exited with code {exit.ExitCode}."),
                        ActiveDeploymentRevision = null
                    };
                    return d.NextRetryAt;
                }, ct).ConfigureAwait(false);
                if (retryAt is { } scheduled) { ScheduleRetryWake(scheduled); }
                await Notify(id).ConfigureAwait(false);
            }
            else { return; }
        }

        state = await Store.ReadAsync(id, ct).ConfigureAwait(false);
        if (state.DesiredState == BehaviorDesiredState.Stopped)
        {
            if (state.State != BehaviorExecutionState.Stopped)
            {
                await Store.UpdateAsync(id, d =>
                {
                    if (d.Snapshot.DesiredState == BehaviorDesiredState.Stopped)
                    { d.Snapshot = d.Snapshot with { State = BehaviorExecutionState.Stopped, Ready = false, GenerationId = null, ActiveDeploymentRevision = null }; }
                    return true;
                }, ct).ConfigureAwait(false);
                await Notify(id).ConfigureAwait(false);
            }
            return;
        }
        if (state.State == BehaviorExecutionState.Completed || state.DesiredDeploymentRevision is null) { return; }
        var canStart = await Store.CanStartAsync(id, ct).ConfigureAwait(false);
        if (!canStart)
        {
            // A retry that is not due yet (an early timer, or a host that restarted mid-backoff) must
            // still wake the loop when it becomes due; nothing else will.
            if (await Store.PendingRetryAsync(id, ct).ConfigureAwait(false) is { } pending) { ScheduleRetryWake(pending); }
            return;
        }
        var generation = Guid.NewGuid();
        try
        {
            var deployment = state.Deployments.Single(x => x.Revision == state.DesiredDeploymentRevision);
            var artifact = await Artifacts.OpenVerifiedAsync(deployment.Artifact, ct).ConfigureAwait(false);
            var reserved = await Store.UpdateAsync(id, d =>
            {
                if (d.Snapshot.Revision != state.Revision || d.Snapshot.DesiredState != BehaviorDesiredState.Running) { return false; }
                d.Snapshot = d.Snapshot with { GenerationId = generation, State = BehaviorExecutionState.Starting, Ready = false, ActiveDeploymentRevision = deployment.Revision };
                return true;
            }, ct).ConfigureAwait(false);
            if (!reserved) { return; }
            var execution = await executor.StartAsync(new(id, generation, artifact, deployment.ConfigurationJson,
                async ready =>
                {
                    await Store.UpdateAsync(id, d =>
                    {
                        if (d.Snapshot.GenerationId == generation && d.Snapshot.DesiredState == BehaviorDesiredState.Running)
                        { d.Snapshot = d.Snapshot with { Ready = ready, State = ready ? BehaviorExecutionState.Running : BehaviorExecutionState.Starting }; }
                        return true;
                    }, CancellationToken.None).ConfigureAwait(false);
                    await Notify(id).ConfigureAwait(false);
                }, async (stream, message) =>
                {
                    var sequence = await Logs.AppendAsync(id, generation, stream, message, CancellationToken.None).ConfigureAwait(false);
                    try { await grains.GetGrain<IBehaviorProgramEvents>(id).LogsChanged(generation, sequence).ConfigureAwait(false); }
                    catch (Exception error) { logger.LogWarning(error, "Log notification failed for {Program}", id); }
                }), ct).ConfigureAwait(false);
            _active[id] = execution;
            _ = ObserveCompletion(execution.Completion);
            await Notify(id).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var retryAt = await Store.UpdateAsync(id, d =>
            {
                if (d.Snapshot.Revision != state.Revision) { return null; }
                d.Retries++;
                d.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, d.Retries - 1));
                d.Snapshot = d.Snapshot with { State = BehaviorExecutionState.Failed, Ready = false, Error = error.Message, ActiveDeploymentRevision = null };
                return d.NextRetryAt;
            }, CancellationToken.None).ConfigureAwait(false);
            if (retryAt is { } scheduled) { ScheduleRetryWake(scheduled); }
            await Notify(id).ConfigureAwait(false);
        }
    }

    public async Task StopProgramAsync(string id, CancellationToken ct)
    {
        if (!_active.TryGetValue(id, out var active)) { return; }
        await executor.StopAsync(active.GenerationId, ct).ConfigureAwait(false);
        await active.Completion.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        _active.Remove(id);
    }

    private async Task Notify(string id)
    {
        try { await grains.GetGrain<IBehaviorProgramEvents>(id).Changed().ConfigureAwait(false); }
        catch (Exception error) { logger.LogWarning(error, "Behavior notification failed for {Program}; persisted state remains authoritative.", id); }
    }

    public override void Dispose() { base.Dispose(); _ownership?.Dispose(); }
}