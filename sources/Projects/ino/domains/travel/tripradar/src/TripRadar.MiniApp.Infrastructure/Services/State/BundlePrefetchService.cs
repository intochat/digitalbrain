using Microsoft.Extensions.Logging;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class BundlePrefetchService(
    IFlightManager flightApi,
    FlightSearchState searchState,
    ILogger<BundlePrefetchService> logger)
{
    private static readonly bool Enabled = false;
    private const int MaxBundles = 5;
    private CancellationTokenSource? _cts;

    public async Task LoadBundlesAsync(FlightSearchResult outboundResults)
    {
        if (!Enabled)
        {
            searchState.IsBundlesLoading = false;
            searchState.Bundles = null;
            searchState.NotifyChanged();
            return;
        }

        var newCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, newCts);
        previous?.Cancel();
        previous?.Dispose();

        var ct = newCts.Token;

        searchState.IsBundlesLoading = true;
        searchState.Bundles = null;
        searchState.NotifyChanged();

        try
        {
            var candidates = PickCandidates(outboundResults);
            if (candidates.Count == 0)
                return;

            var tasks = candidates.Select(outbound => FetchReturnAsync(outbound, ct)).ToList();
            var results = await Task.WhenAll(tasks);

            if (ct.IsCancellationRequested)
                return;

            var pairs = results
                .Where(r => r is not null)
                .Select(r => r!.Value)
                .ToList();

            searchState.Bundles = FlightBundle.Create(pairs);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                searchState.IsBundlesLoading = false;
                searchState.NotifyChanged();
            }
        }
    }

    public void Cancel()
    {
        var previous = Interlocked.Exchange(ref _cts, null);
        previous?.Cancel();
        previous?.Dispose();
    }

    private static List<FlightOption> PickCandidates(FlightSearchResult results)
    {
        var all = (results.BestFlights ?? [])
            .Concat(results.OtherFlights ?? [])
            .Where(f => !string.IsNullOrEmpty(f.DepartureToken))
            .Take(MaxBundles)
            .ToList();

        return all;
    }

    private async Task<(FlightOption Outbound, FlightOption Return)?> FetchReturnAsync(
        FlightOption outbound, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await flightApi.SearchAsync(searchState.Params, outbound.DepartureToken, ct);

            if (result is null) return null;

            var bestReturn = result.BestFlights?.FirstOrDefault()
                          ?? result.OtherFlights?.FirstOrDefault();

            return bestReturn is not null ? (outbound, bestReturn) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch return flights for outbound token {DepartureToken}",
                outbound.DepartureToken);
            return null;
        }
    }
}