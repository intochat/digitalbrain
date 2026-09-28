namespace DigitalBrain.Discovery;

// A searchable capability. WorkspaceId null means every workspace can see it.
public sealed record CapabilityDocument(string Id, CapabilityKind Kind, string Name, string Description, string? WorkspaceId = null);

// Apps, neuron contracts and anything else the assistant can reach publish themselves through a source.
public interface ICapabilitySource
{
    Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken);
}
