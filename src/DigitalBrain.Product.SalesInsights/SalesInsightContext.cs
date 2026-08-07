namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// A frozen opaque destination for a sales result, never a Hosting scope.
/// </summary>
public sealed record SalesInsightContext
{
    public SalesInsightContext(SalesInsightContextKind kind, string reference)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The sales insight context kind is not recognized.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        Kind = kind;
        Reference = reference.Trim();
    }

    public SalesInsightContextKind Kind { get; }

    public string Reference { get; }
}
