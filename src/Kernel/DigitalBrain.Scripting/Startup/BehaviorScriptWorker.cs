using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Scripting.Startup;

internal sealed class BehaviorScriptWorker(
    IBehaviorAdmissionSource admissions,
    IStartupScriptRunner runner,
    IDigitalBrain brain,
    ILogger<BehaviorScriptWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RunningBehavior> _running = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var admitted in admissions
            .WatchAsync(stoppingToken)
            .WithCancellation(stoppingToken))
        {
            var script = StartupScript.FromSource(admitted.Name, admitted.Source);
            if (_running.TryGetValue(admitted.Name, out var existing)
                && string.Equals(existing.Sha256, script.Sha256, StringComparison.Ordinal))
            {
                continue;
            }

            if (existing is not null)
            {
                await existing.DisposeAsync().ConfigureAwait(false);
            }

            var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var executing = RunAsync(admitted.Name, script, run.Token);
            _running[admitted.Name] = new RunningBehavior(script.Sha256, run, executing);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var running in _running.Values)
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }

        _running.Clear();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(string name, StartupScript script, CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.RunAsync(script, brain, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Behavior {BehaviorName} completed: {Summary}",
                    name,
                    result.Summary);
                return;
            }

            logger.LogError(
                "Behavior {BehaviorName} failed: {Summary} {Diagnostics}",
                name,
                result.Summary,
                string.Join("; ", result.Diagnostics));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Behavior {BehaviorName} terminated unexpectedly", name);
        }
    }

    private sealed class RunningBehavior(string sha256, CancellationTokenSource cancellation, Task executing)
        : IAsyncDisposable
    {
        public string Sha256 { get; } = sha256;

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await executing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
