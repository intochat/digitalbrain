using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Product.Conversation;

/// <summary>
/// An explicitly external chat command with an already-resolved sales query.
/// </summary>
public sealed record ChatSalesRequested : Synapse
{
    public ChatSalesRequested(SalesQuery query)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public SalesQuery Query { get; }
}
