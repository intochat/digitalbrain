using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.workspaces")]
public interface IWorkspaces : INeuron
{
    /// <summary>Ensures a workspace exists and returns its refreshed record.</summary>
    [Alias("ensure")]
    [NeuronTool]
    Task<Accepted<WorkspaceRecord>> Ensure(EnsureWorkspace command, CancellationToken cancellationToken = default);

    /// <summary>Finds a workspace by name or correlation id.</summary>
    [ReadOnly, Alias("find")]
    [NeuronTool(IsReadOnly = true)]
    Task<WorkspaceRecord?> Find(FindWorkspace query);
}
