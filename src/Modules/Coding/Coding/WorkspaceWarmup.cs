using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Coding;

// Opens the configured solution when the silo starts so the first question does not pay for the load.
internal sealed class WorkspaceWarmup(SolutionWorkspace workspace, IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = configuration[CodingModule.SolutionPathKey];
        if (!string.IsNullOrWhiteSpace(path))
        {
            _ = workspace.BeginOpenAsync(Path.GetFullPath(path));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
