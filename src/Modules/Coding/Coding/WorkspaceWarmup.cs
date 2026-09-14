using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

// Opens the configured solution when the silo starts so the first question does not pay for the load.
internal sealed class WorkspaceWarmup(SolutionWorkspace workspace, SolutionFileWatcher watcher, IGrainFactory grains, IConfiguration configuration, ILogger<WorkspaceWarmup> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        workspace.Opened = path => watcher.Start(Path.GetDirectoryName(path)!);
        var path = configuration[CodingModule.SolutionPathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(error, "The configured solution path '{SolutionPath}' is not a valid path; the workspace stays closed until it is opened by hand.", path);
            return Task.CompletedTask;
        }

        _ = workspace.BeginOpenAsync(fullPath);
        _ = RecordOpenAsync(fullPath, configuration[CodingModule.WorkspaceKeyKey] ?? "digitalbrain");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        watcher.Stop();
        return Task.CompletedTask;
    }

    // The grain is the durable record of "which solution is open"; the load itself already runs in the service,
    // so this open is a no-op for the service and records the path, generation and map in grain state.
    private async Task RecordOpenAsync(string fullPath, string key)
    {
        try
        {
            await workspace.WhenReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (WorkspaceNotReadyException)
        {
            return;
        }

        var grain = grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, key).ToGrainId());
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await grain.Open(new OpenWorkspace(CommandId.New(), fullPath)).ConfigureAwait(false);
                return;
            }
#pragma warning disable CA1031 // The silo may still be coming up; every exception but cancellation is retried up to the bound below.
            catch (Exception error) when (error is not OperationCanceledException && attempt < 29)
#pragma warning restore CA1031
            {
                // The silo is still coming up; a grain call before membership settles is retried.
                logger.LogDebug(error, "Recording the open of {SolutionPath} on {Key} failed; retrying.", fullPath, key);
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
    }
}
