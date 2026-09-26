using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;

namespace IntoChat.Apps;

// Wraps the external web-research adapter. Every search, browse or lookup is one metered
// search.request; the provider result is returned only after the meter is recorded.
internal sealed class WebResearchService(IWebResearchProvider provider, IMeterSink meters, TimeProvider time) : IWebResearch
{
    public ValueTask<WebResearchResult> SearchAsync(WebResearchQuery query, CallerContext caller, CancellationToken cancellationToken = default)
        => MeasureAsync(caller, () => provider.SearchAsync(query, cancellationToken), cancellationToken);

    public ValueTask<WebResearchResult> BrowseAsync(BrowseWebRequest request, CallerContext caller, CancellationToken cancellationToken = default)
        => MeasureAsync(caller, () => provider.BrowseAsync(request, cancellationToken), cancellationToken);

    public ValueTask<WebResearchResult> LookupCompanyAsync(CompanyLookup request, CallerContext caller, CancellationToken cancellationToken = default)
        => MeasureAsync(caller, () => provider.LookupCompanyAsync(request, cancellationToken), cancellationToken);

    private async ValueTask<WebResearchResult> MeasureAsync(CallerContext caller, Func<Task<WebResearchResult>> call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var result = await call().ConfigureAwait(false);
        await meters.RecordAsync(new MeterEvent
        {
            IntentId = string.IsNullOrEmpty(caller.IntentId) ? "intochat.leadgenerator" : caller.IntentId,
            MeterId = WebResearchMeters.SearchRequest,
            Step = "request-" + Guid.NewGuid().ToString("n"),
            WorkspaceId = caller.WorkspaceId,
            Quantity = 1,
            Unit = WebResearchMeters.RequestUnit,
            Source = MeterSource.CallFilter,
            AppId = caller.AppId,
            CostBasis = result.Provider,
            OccurredAt = time.GetUtcNow(),
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
