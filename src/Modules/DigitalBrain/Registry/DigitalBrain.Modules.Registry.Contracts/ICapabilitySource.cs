namespace DigitalBrain.Registry;

// A searchable capability. WorkspaceId null means every workspace can see it.
// Tools are the agent tool names a turn gets when it finds this capability.
public sealed record CapabilityDocument(string Id, CapabilityKind Kind, string Name, string Description,
    string? WorkspaceId = null, IReadOnlyList<string>? Tools = null);

// Apps, neuron contracts and anything else the assistant can reach publish themselves through a source.
public interface ICapabilitySource
{
    Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken);

    // Calls changed whenever what Read returns may differ, until cancelled. A source whose
    // capabilities are fixed for the host's lifetime keeps the default.
    Task Watch(Action changed, CancellationToken cancellationToken) => Task.CompletedTask;
}