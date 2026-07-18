using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class FlightPrefetchService(
    IFlightManager flightApi,
    FlightSearchState searchState) : IDisposable
{
    private static readonly bool Enabled = false;

    private Timer? _debounceTimer;
    private string? _inflightFingerprint;

    public void RequestPrefetch(FlightSearchParams currentParams)
    {
        if (!Enabled) return;

        if (currentParams.Type == FlightType.MultiCity) return;

        if (!IsReadyToPrefetch(currentParams))
            return;

        var fingerprint = ComputeFingerprint(currentParams);

        if (fingerprint == searchState.PrefetchFingerprint
            || fingerprint == _inflightFingerprint)
            return;

        Cancel();

        var snapshot = CloneParams(currentParams);
        _debounceTimer = new Timer(
            _ => _ = ExecuteAsync(snapshot, fingerprint),
            null,
            300,
            Timeout.Infinite);
    }

    public void Cancel()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _inflightFingerprint = null;
    }

    public static string ComputeFingerprint(FlightSearchParams p)
    {
        var returnDate = p.Type == FlightType.RoundTrip ? p.ReturnDate : "";
        return $"{p.DepartureId}|{p.ArrivalId}|{p.OutboundDate}|{returnDate}|{(int)p.Type}|{p.Adults}|{p.Children}|{p.Infants}|{(int)p.TravelClass}";
    }

    public void Dispose() => Cancel();

    private async Task ExecuteAsync(FlightSearchParams snapshot, string fingerprint)
    {
        _inflightFingerprint = fingerprint;

        try
        {
            var searchSnapshot = snapshot.Type == FlightType.RoundTrip
            ? new FlightSearchParams
            {
                DepartureId = snapshot.DepartureId,
                ArrivalId = snapshot.ArrivalId,
                DepartureName = snapshot.DepartureName,
                ArrivalName = snapshot.ArrivalName,
                DepartureCountryCode = snapshot.DepartureCountryCode,
                ArrivalCountryCode = snapshot.ArrivalCountryCode,
                OutboundDate = snapshot.OutboundDate,
                Type = FlightType.OneWay,
                TravelClass = snapshot.TravelClass,
                Adults = snapshot.Adults,
                Children = snapshot.Children,
                Infants = snapshot.Infants
            }
            : snapshot;
        var result = await flightApi.SearchAsync(searchSnapshot);

            if (result is not null && _inflightFingerprint == fingerprint)
                searchState.SetPrefetchedResults(result, fingerprint);
        }
        catch
        {
            // swallow — results page will fetch normally
        }
        finally
        {
            if (_inflightFingerprint == fingerprint)
                _inflightFingerprint = null;
        }
    }

    private static bool IsReadyToPrefetch(FlightSearchParams p)
    {
        if (string.IsNullOrEmpty(p.DepartureId) || string.IsNullOrEmpty(p.ArrivalId))
            return false;

        if (string.IsNullOrEmpty(p.OutboundDate))
            return false;

        if (p.Type == FlightType.RoundTrip && string.IsNullOrEmpty(p.ReturnDate))
            return false;

        return true;
    }

    private static FlightSearchParams CloneParams(FlightSearchParams p) => new()
    {
        DepartureId = p.DepartureId,
        ArrivalId = p.ArrivalId,
        DepartureName = p.DepartureName,
        ArrivalName = p.ArrivalName,
        DepartureCountryCode = p.DepartureCountryCode,
        ArrivalCountryCode = p.ArrivalCountryCode,
        OutboundDate = p.OutboundDate,
        ReturnDate = p.ReturnDate,
        Type = p.Type,
        TravelClass = p.TravelClass,
        Adults = p.Adults,
        Children = p.Children,
        Infants = p.Infants
    };
}