using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.workspaces")]
public interface IWorkspaces : INeuron
{
    /// <summary>Ensures a workspace exists and returns its refreshed record.</summary>
    [Alias("ensure")]
    Task<Accepted<WorkspaceRecord>> Ensure(EnsureWorkspace command, CancellationToken cancellationToken = default);

    /// <summary>Finds a workspace by name or correlation id.</summary>
    [ReadOnly, Alias("find")]
    Task<WorkspaceRecord?> Find(FindWorkspace query);
}
