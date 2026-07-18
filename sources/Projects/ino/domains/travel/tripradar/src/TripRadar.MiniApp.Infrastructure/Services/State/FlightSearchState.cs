using TripRadar.MiniApp.Client.Infrastructure.Models.Common;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class FlightSearchState
{
    public FlightSearchParams Params { get; set; } = new();
    public List<ItineraryFlight> Itinerary { get; set; } = [];
    public int CurrentFlightIndex { get; set; }
    public FlightSearchResult? Results { get; set; }
    public bool IsLoading { get; set; }
    public string? Error { get; set; }
    public CitySuggestion? PendingCitySuggestion { get; set; }
    public string? PendingSearchQuery { get; set; }
    public List<CitySuggestion>? PendingSearchResults { get; set; }
    public List<MultiCityLeg> MultiCityLegs { get; set; } = [new(), new()];
    public FlightSearchResult? PrefetchedResults { get; private set; }
    public string? PrefetchFingerprint { get; private set; }
    public RoundTripMode RoundTripMode { get; set; } = RoundTripMode.PairedDeals;
    public PairedSortOrder PairedSort { get; set; } = PairedSortOrder.Best;
    public List<FlightBundle>? Bundles { get; set; }
    public bool IsBundlesLoading { get; set; }

    public event Action? OnChanged;

    public ItineraryFlight? CurrentItineraryFlight =>
        CurrentFlightIndex >= 0 && CurrentFlightIndex < Itinerary.Count
            ? Itinerary[CurrentFlightIndex]
            : null;

    public bool IsMultiLeg => Itinerary.Count > 1;
    public bool IsLastFlight => CurrentFlightIndex >= Itinerary.Count - 1;
    public bool AllFlightsBooked => Itinerary.Count > 0 && Itinerary.All(f => f.IsBooked);
    public bool IsPairedMode => Params.Type == FlightType.RoundTrip && RoundTripMode == RoundTripMode.PairedDeals;
    public bool IsBundleMode => Params.Type == FlightType.RoundTrip && RoundTripMode == RoundTripMode.Bundles;
    public bool UsesRoundTripToken => IsPairedMode || IsBundleMode;
    public bool HasPendingBooking =>
        Itinerary.Any(f => f.SelectedFlight is not null) && !AllFlightsBooked;
    public decimal TotalPrice => Itinerary.Where(f => f.SelectedFlight is not null).Sum(f => f.SelectedFlight!.Price);

    public void NotifyChanged() => OnChanged?.Invoke();

    public void BuildItinerary()
    {
        Itinerary.Clear();
        CurrentFlightIndex = 0;

        if (Params.Type == FlightType.OneWay)
        {
            Itinerary.Add(new ItineraryFlight
            {
                LegType = LegType.Outbound,
                Index = 0,
                SearchParams = CloneParamsAsOneWay(Params.DepartureId, Params.ArrivalId,
                    Params.DepartureName, Params.ArrivalName,
                    Params.DepartureCountryCode, Params.ArrivalCountryCode,
                    Params.OutboundDate)
            });
        }
        else if (Params.Type == FlightType.RoundTrip)
        {
            Itinerary.Add(new ItineraryFlight
            {
                LegType = LegType.Outbound,
                Index = 0,
                SearchParams = CloneParamsAsOneWay(Params.DepartureId, Params.ArrivalId,
                    Params.DepartureName, Params.ArrivalName,
                    Params.DepartureCountryCode, Params.ArrivalCountryCode,
                    Params.OutboundDate)
            });
            Itinerary.Add(new ItineraryFlight
            {
                LegType = LegType.Return,
                Index = 1,
                SearchParams = CloneParamsAsOneWay(Params.ArrivalId, Params.DepartureId,
                    Params.ArrivalName, Params.DepartureName,
                    Params.ArrivalCountryCode, Params.DepartureCountryCode,
                    Params.ReturnDate)
            });
        }
        else if (Params.Type == FlightType.MultiCity)
        {
            for (var i = 0; i < MultiCityLegs.Count; i++)
            {
                var leg = MultiCityLegs[i];
                Itinerary.Add(new ItineraryFlight
                {
                    LegType = LegType.MultiCityLeg,
                    Index = i,
                    SearchParams = CloneParamsAsOneWay(leg.DepartureId, leg.ArrivalId,
                        leg.DepartureName, leg.ArrivalName,
                        leg.DepartureCountryCode, leg.ArrivalCountryCode,
                        leg.Date)
                });
            }
        }
    }

    public void AddMultiCityLeg()
    {
        if (MultiCityLegs.Count >= 5) return;
        var prev = MultiCityLegs[^1];
        MultiCityLegs.Add(new MultiCityLeg
        {
            DepartureId = prev.ArrivalId,
            DepartureName = prev.ArrivalName,
            DepartureCountryCode = prev.ArrivalCountryCode
        });
    }

    public void RemoveMultiCityLeg(int index)
    {
        if (index <= 0 || index >= MultiCityLegs.Count || MultiCityLegs.Count <= 2) return;
        MultiCityLegs.RemoveAt(index);
    }

    public void SelectFlight(FlightOption flight)
    {
        if (CurrentItineraryFlight is { } current)
        {
            current.SelectedFlight = flight;
            OnChanged?.Invoke();
        }
    }

    public void GoToFlight(int index)
    {
        if (index < 0 || index >= Itinerary.Count) return;

        if (index < CurrentFlightIndex)
        {
            for (var i = index + 1; i < Itinerary.Count; i++)
            {
                Itinerary[i].Results = null;
                Itinerary[i].SelectedFlight = null;
                Itinerary[i].BookingProviders = null;
                Itinerary[i].IsBooked = false;
                Itinerary[i].BookedVia = null;
            }

            Bundles = null;
        }

        CurrentFlightIndex = index;
        Error = null;
        OnChanged?.Invoke();
    }

    public void NextFlight()
    {
        if (!IsLastFlight)
        {
            CurrentFlightIndex++;
            Error = null;
            OnChanged?.Invoke();
        }
    }

    public void SetCurrentLoading()
    {
        if (CurrentItineraryFlight is { } current)
        {
            current.IsLoading = true;
            Error = null;
            OnChanged?.Invoke();
        }
    }

    public void SetCurrentResults(FlightSearchResult results)
    {
        if (CurrentItineraryFlight is { } current)
        {
            current.Results = results;
            current.IsLoading = false;
            Error = null;
            OnChanged?.Invoke();
        }
    }

    public void SetCurrentError(string error)
    {
        if (CurrentItineraryFlight is { } current)
            current.IsLoading = false;
        Error = error;
        OnChanged?.Invoke();
    }

    public void MarkCurrentBooked(string? providerName)
    {
        if (CurrentItineraryFlight is { } current)
        {
            current.IsBooked = true;
            current.BookedVia = providerName;
            OnChanged?.Invoke();
        }
    }

    public int? NextUnbookedIndex
    {
        get
        {
            for (var i = 0; i < Itinerary.Count; i++)
                if (!Itinerary[i].IsBooked) return i;
            return null;
        }
    }

    public void SetLoading()
    {
        IsLoading = true;
        Error = null;
        OnChanged?.Invoke();
    }

    public void SetResults(FlightSearchResult results)
    {
        Results = results;
        IsLoading = false;
        Error = null;
        OnChanged?.Invoke();
    }

    public void SetError(string error)
    {
        Error = error;
        IsLoading = false;
        OnChanged?.Invoke();
    }

    public void SetPrefetchedResults(FlightSearchResult results, string fingerprint)
    {
        PrefetchedResults = results;
        PrefetchFingerprint = fingerprint;
    }

    public bool TryConsumePrefetch(string fingerprint, out FlightSearchResult results)
    {
        if (PrefetchFingerprint == fingerprint && PrefetchedResults is not null)
        {
            results = PrefetchedResults;
            PrefetchedResults = null;
            PrefetchFingerprint = null;
            return true;
        }

        results = default!;
        return false;
    }

    public void Reset()
    {
        Params = new();
        Results = null;
        Itinerary.Clear();
        MultiCityLegs = [new(), new()];
        CurrentFlightIndex = 0;
        IsLoading = false;
        Error = null;
        PrefetchedResults = null;
        PrefetchFingerprint = null;
        RoundTripMode = RoundTripMode.PairedDeals;
        PairedSort = PairedSortOrder.Best;
        Bundles = null;
        IsBundlesLoading = false;
        OnChanged?.Invoke();
    }

    private FlightSearchParams CloneParamsAsOneWay(
        string depId, string arrId,
        string depName, string arrName,
        string? depCountry, string? arrCountry,
        string date) => new()
    {
        DepartureId = depId,
        ArrivalId = arrId,
        DepartureName = depName,
        ArrivalName = arrName,
        DepartureCountryCode = depCountry,
        ArrivalCountryCode = arrCountry,
        OutboundDate = date,
        Type = FlightType.OneWay,
        TravelClass = Params.TravelClass,
        Adults = Params.Adults,
        Children = Params.Children,
        Infants = Params.Infants
    };
}
