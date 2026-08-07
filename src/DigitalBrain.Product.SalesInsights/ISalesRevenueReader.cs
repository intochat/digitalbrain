namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// The narrow provider seam for reading one typed closed-won revenue query.
/// </summary>
public interface ISalesRevenueReader
{
    Task<IReadOnlyList<SalesRevenueRecord>> ReadClosedWonAsync(
        SalesQuery query,
        CancellationToken cancellationToken);
}
