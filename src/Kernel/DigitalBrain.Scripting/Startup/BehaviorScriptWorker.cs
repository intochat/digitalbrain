using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Scripting.Startup;

internal sealed class BehaviorScriptWorker(
    IBehaviorAdmissionSource admissions,
    IStartupScriptRunner runner,
    IDigitalBrain brain,
    ILogger<BehaviorScriptWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CancellationGrace = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, RunningBehavior> _running = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var snapshot in admissions.WatchAsync(stoppingToken).WithCancellation(stoppingToken))
            {
                var current = snapshot.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
                foreach (var (name, existing) in _running.ToArray())
                {
                    if (current.TryGetValue(name, out var definition) && definition.Revision == existing.Revision)
                    {
                        continue;
                    }

                    if (await StopAsync(name, existing, stoppingToken).ConfigureAwait(false))
                    {
                        _running.Remove(name);
                    }
                    else if (definition is not null)
                    {
                        await admissions.ReportAsync(new ReportBehaviorStatus(
                            name,
                            definition.Revision,
                            BehaviorStatus.Failed,
                            "The previous execution has not stopped. It must observe CancellationToken before replacement can run.",
                            []), stoppingToken).ConfigureAwait(false);
                    }
                }

                foreach (var definition in snapshot)
                {
                    if (_running.ContainsKey(definition.Name)
                        || definition.Status is BehaviorStatus.Completed or BehaviorStatus.Failed)
                    {
                        continue;
                    }

                    // Stop executions explicitly below: linking directly to the host token
                    // would let a throwing script callback escape BackgroundService.StopAsync.
                    var cancellation = new CancellationTokenSource();
                    // User C# may run synchronously before its first await. Keep it off
                    // the admission loop so cancellation and other programs still work.
                    var executing = Task.Run(() => RunAsync(definition, cancellation.Token), CancellationToken.None);
                    _running[definition.Name] = new RunningBehavior(definition.Revision, cancellation, executing);
                }
            }
        }
        finally
        {
            await Task.WhenAll(_running.Select(pair => StopAsync(pair.Key, pair.Value, CancellationToken.None)))
                .ConfigureAwait(false);
            _running.Clear();
        }
    }

    private async Task<bool> StopAsync(string name, RunningBehavior running, CancellationToken cancellationToken)
    {
        // CancelAsync schedules user cancellation callbacks away from this loop.
        var cancelling = CancelCallbacksAsync();
        try
        {
            await Task.WhenAll(cancelling, running.Executing)
                .WaitAsync(CancellationGrace, cancellationToken).ConfigureAwait(false);
            running.Cancellation.Dispose();
            return true;
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Behavior {BehaviorName} is still executing after cancellation was requested; its script must observe CancellationToken",
                name);
            return false;
        }

        async Task CancelCallbacksAsync()
        {
            try
            {
                await running.Cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // User callbacks may throw; still wait for the script to exit and
                // keep the other admitted programs and the host alive.
                logger.LogWarning(exception, "Cancellation callback failed for behavior {BehaviorName}", name);
            }
        }
    }

    private async Task RunAsync(BehaviorDefinition definition, CancellationToken cancellationToken)
    {
        // Restore the actual admitting principal, never identity supplied by script source.
        using var actor = VerifiedActor.Enter(definition.Principal is { } principal
            ? new ActorContext(principal, "_behavior")
            : null);
        try
        {
            await ReportAsync(BehaviorStatus.Running, "Compiling and running.", []).ConfigureAwait(false);
            var result = await runner.RunAsync(
                StartupScript.FromSource(definition.Name, definition.Source), brain, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await ReportAsync(
                result.IsSuccess ? BehaviorStatus.Completed : BehaviorStatus.Failed,
                result.Summary,
                result.Diagnostics).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A still-admitted Running revision resumes when the scripting host restarts.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Behavior {BehaviorName} failed", definition.Name);
            try
            {
                await ReportAsync(BehaviorStatus.Failed, exception.Message, []).ConfigureAwait(false);
            }
            catch (Exception reportFailure)
            {
                logger.LogError(reportFailure, "Could not record failure for behavior {BehaviorName}", definition.Name);
            }
        }

        Task ReportAsync(BehaviorStatus status, string summary, IReadOnlyList<string> diagnostics)
            => admissions.ReportAsync(new ReportBehaviorStatus(
                definition.Name, definition.Revision, status, summary, diagnostics), cancellationToken);
    }

    private sealed record RunningBehavior(Guid Revision, CancellationTokenSource Cancellation, Task Executing);
}
