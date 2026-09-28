using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal sealed class IdleShutdown(SandboxRuns runs, IOptions<SandboxOptions> options, IHostApplicationLifetime lifetime, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.IdleShutdown is not { } idle) { return; }
        var idleSince = time.GetUtcNow();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (runs.AnyRunning) { idleSince = time.GetUtcNow(); }
                else if (time.GetUtcNow() - idleSince >= idle) { lifetime.StopApplication(); return; }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
