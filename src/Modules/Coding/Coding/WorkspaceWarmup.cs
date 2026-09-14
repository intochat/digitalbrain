using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

// Opens the configured solution when the silo starts so the first question does not pay for the load.
internal sealed class WorkspaceWarmup(SolutionWorkspace workspace, IConfiguration configuration, ILogger<WorkspaceWarmup> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = configuration[CodingModule.SolutionPathKey];
        if (!string.IsNullOrWhiteSpace(path))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Warmup is best-effort: a bad configured path degrades to a closed workspace, not a silo that never starts.
                logger.LogWarning(error, "The configured solution path '{SolutionPath}' is not a valid path; the workspace stays closed until it is opened by hand.", path);
                return Task.CompletedTask;
            }

            _ = workspace.BeginOpenAsync(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
