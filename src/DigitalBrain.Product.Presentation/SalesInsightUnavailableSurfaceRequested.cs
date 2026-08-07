using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Product.Presentation;

/// <summary>
/// A redacted, renderer-neutral unavailable state; it never turns missing data
/// into a zero-value chart.
/// </summary>
public sealed record SalesInsightUnavailableSurfaceRequested(
    string QueryId,
    SalesInsightContext Context,
    SalesInsightUnavailableReason Reason,
    IReadOnlyList<SalesInsightPlacement> Placements) : Synapse;
