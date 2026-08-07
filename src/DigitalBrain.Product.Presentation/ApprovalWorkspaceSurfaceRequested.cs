namespace DigitalBrain.Product.Presentation;

public sealed record ApprovalWorkspaceSurfaceRequested : Synapse
{
    public ApprovalWorkspaceSurfaceRequested(
        long revision,
        IReadOnlyList<ApprovalWorkspaceSurfaceItem> items)
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "A workspace surface needs a positive revision.");
        }

        ArgumentNullException.ThrowIfNull(items);
        var itemCopy = items.ToArray();
        if (itemCopy.Any(static item => item is null))
        {
            throw new ArgumentException("A workspace surface cannot contain null items.", nameof(items));
        }

        Revision = revision;
        Items = Array.AsReadOnly(itemCopy);
    }

    public long Revision { get; }

    public IReadOnlyList<ApprovalWorkspaceSurfaceItem> Items { get; }
}
