using System.Globalization;
using System.Text.Json;
using AutoMapper;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Mappings;

public class FlightsQueryProfile : Profile
{
    public FlightsQueryProfile()
    {
        CreateMap<ScheduledFlightQuery, GetFlightRequestDTO>()
            .ForMember(dest => dest.Username, opt => opt.MapFrom(src => src.User.Profile.Username))
            .ForMember(dest => dest.FlightSearch,
                opt => opt.MapFrom(src => new FlightSearchQueryDTO
                {
                    DepartureId = src.DepartureAirport.Code.ToUpperInvariant(),
                    ArrivalId = src.DestinationAirport.Code.ToUpperInvariant()
                }))
            .ForMember(dest => dest.AdvancedOptions,
                opt => opt.MapFrom(src => new AdvancedSearchOptions
                {
                    OutboundDate = src.DepartureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ReturnDate = src.ReturnDate.HasValue ? src.ReturnDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    Type = src.ReturnDate.HasValue ? FlightType.RoundTrip : FlightType.OneWay,
                    TravelClass = GetTravelClass(src.AdditionalParameters),
                    MultiCityJson = GetMultiCityLegs(src.AdditionalParameters),
                    ShowHidden = src.AdditionalParameters.GetParameter<bool?>("show_hidden_results"),
                    DeepSearch = src.AdditionalParameters.GetParameter<bool?>("deep_search")
                }))
            .ForMember(dest => dest.Passengers,
                opt => opt.MapFrom(src => new PassengerInfo
                {
                    Adults = src.AdditionalParameters.GetParameter<int?>("adults") ?? 1,
                    Children = src.AdditionalParameters.GetParameter<int?>("children") ?? 0,
                    InfantsInSeat = src.AdditionalParameters.GetParameter<int?>("infants_in_seat") ?? 0,
                    InfantsOnLap = src.AdditionalParameters.GetParameter<int?>("infants_on_lap") ?? 0
                }))
            .ForMember(dest => dest.Localization,
                opt => opt.MapFrom(src => new Localization
                {
                    Currency = src.AdditionalParameters.GetParameter<string>("currency"),
                    Hl = src.AdditionalParameters.GetParameter<string>("hl"),
                    Gl = src.AdditionalParameters.GetParameter<string>("gl")
                }))
            .ForMember(dest => dest.Sorting,
                opt => opt.MapFrom(src =>
                    new SortingOptions
                    {
                        SortBy = GetSortBy(src.AdditionalParameters.GetParameter<string>("sort_by"))
                    }))
            .ForMember(dest => dest.Filters,
                opt => opt.MapFrom(src => new AdvancedFilters
                {
                    MaxPrice = src.AdditionalParameters.GetParameter<int?>("max_price"),
                    Stops = GetStops(src.AdditionalParameters.GetParameter<string>("stops")),
                    IncludeAirlines = src.AdditionalParameters.GetParameter<string>("include_airlines"),
                    ExcludeAirlines = src.AdditionalParameters.GetParameter<string>("exclude_airlines"),
                    Bags = src.AdditionalParameters.GetParameter<int?>("bags"),
                    OutboundTimes = src.AdditionalParameters.GetParameter<string>("outbound_times"),
                    ReturnTimes = src.AdditionalParameters.GetParameter<string>("return_times"),
                    MaxDuration = src.AdditionalParameters.GetParameter<int?>("max_duration"),
                    LayoverDuration = src.AdditionalParameters.GetParameter<string>("layover_duration"),
                    Emissions = src.AdditionalParameters.GetParameter<int?>("emissions")
                }))
            .ForMember(dest => dest.NextFlights, opt => opt.MapFrom(src =>
                src.AdditionalParameters.GetParameter<string>("departure_token") != null
                    ? new NextFlights
                    {
                        DepartureToken = src.AdditionalParameters.GetParameter<string>("departure_token")
                    }
                    : null))
            .ForMember(dest => dest.Booking, opt => opt.MapFrom(src =>
                src.AdditionalParameters.GetParameter<string>("booking_token") != null
                    ? new BookingFlights
                    {
                        BookingToken = src.AdditionalParameters.GetParameter<string>("booking_token")
                    }
                    : null));
    }

    private static TravelClassType? GetTravelClass(string? additionalParameters)
    {
        var travelClass = additionalParameters.GetParameter<string>("travel_class");
        if (string.IsNullOrEmpty(travelClass))
        {
            return null;
        }

        return travelClass.ToLowerInvariant() switch
        {
            "economy" => TravelClassType.Economy,
            "premium_economy" => TravelClassType.PremiumEconomy,
            "business" => TravelClassType.Business,
            "first" => TravelClassType.First,
            _ => TravelClassType.Economy
        };
    }

    private static List<MultiCityLeg>? GetMultiCityLegs(string? additionalParameters)
    {
        var multiCityJson = additionalParameters.GetParameter<string>("multi_city_json");
        if (string.IsNullOrEmpty(multiCityJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<MultiCityLeg>>(multiCityJson);
        }
        catch
        {
            return null;
        }
    }

    private static SortBy? GetSortBy(string? sortBy)
    {
        if (string.IsNullOrEmpty(sortBy))
        {
            return null;
        }

        return sortBy.ToLowerInvariant() switch
        {
            "top_flights" => SortBy.TopFlights,
            "price" => SortBy.Price,
            "departure_time" => SortBy.DepartureTime,
            "arrival_time" => SortBy.ArrivalTime,
            "duration" => SortBy.Duration,
            "emissions" => SortBy.Emissions,
            _ => SortBy.TopFlights
        };
    }

    private static StopsType? GetStops(string? stops)
    {
        if (string.IsNullOrEmpty(stops))
        {
            return null;
        }

        return stops switch
        {
            "0" => StopsType.Any,
            "1" => StopsType.Nonstop,
            "2" => StopsType.OneStopOrFewer,
            "3" => StopsType.TwoStopsOrFewer,
            _ => StopsType.Any
        };
    }
}
