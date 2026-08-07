namespace DigitalBrain;

public sealed class WorkspaceBinding
{
    internal WorkspaceBinding(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }
}
