using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class PreferenceType(int id, string name) : Enumeration(id, name)
{
    public static readonly PreferenceType Adults = new(1, nameof(Adults));
    public static readonly PreferenceType Children = new(2, nameof(Children));
    public static readonly PreferenceType InfantsInSeat = new(3, nameof(InfantsInSeat));
    public static readonly PreferenceType InfantsOnLap = new(4, nameof(InfantsOnLap));
    public static readonly PreferenceType MaxPrice = new(5, nameof(MaxPrice));
    public static readonly PreferenceType Currency = new(6, nameof(Currency));
    public static readonly PreferenceType TravelClass = new(7, nameof(TravelClass));
    public static readonly PreferenceType SortBy = new(8, nameof(SortBy));
    public static readonly PreferenceType PreferredCabinClass = new(9, nameof(PreferredCabinClass));
    public static readonly PreferenceType PreferredAirlines = new(10, nameof(PreferredAirlines));
    public static readonly PreferenceType MaxLayovers = new(11, nameof(MaxLayovers));
    public static readonly PreferenceType PreferredDepartureTime = new(12, nameof(PreferredDepartureTime));
    public static readonly PreferenceType PreferredArrivalTime = new(13, nameof(PreferredArrivalTime));
    public static readonly PreferenceType DefaultInfantsInSeat = new(14, nameof(DefaultInfantsInSeat));
    public static readonly PreferenceType DefaultInfantsOnLap = new(15, nameof(DefaultInfantsOnLap));
    public static readonly PreferenceType DefaultBags = new(16, nameof(DefaultBags));
    public static readonly PreferenceType AvoidAirlines = new(17, nameof(AvoidAirlines));
    public static readonly PreferenceType PreferredDepartureAirportCode = new(18, nameof(PreferredDepartureAirportCode));

    public static readonly PreferenceType MinPrice = new(101, nameof(MinPrice));
    public static readonly PreferenceType FreeCancellation = new(102, nameof(FreeCancellation));
    public static readonly PreferenceType Rating = new(103, nameof(Rating));
    public static readonly PreferenceType DefaultRooms = new(104, nameof(DefaultRooms));
    public static readonly PreferenceType PreferredStarRating = new(105, nameof(PreferredStarRating));
    public static readonly PreferenceType PreferredAmenities = new(106, nameof(PreferredAmenities));
    public static readonly PreferenceType PreferredHotelChains = new(107, nameof(PreferredHotelChains));
    public static readonly PreferenceType MaxPricePerNight = new(108, nameof(MaxPricePerNight));
    public static readonly PreferenceType PreferredRoomType = new(109, nameof(PreferredRoomType));

    public static readonly PreferenceType Language = new(201, nameof(Language));
    public static readonly PreferenceType PreferredCategories = new(202, nameof(PreferredCategories));
    public static readonly PreferenceType PreferredEventTypes = new(203, nameof(PreferredEventTypes));
    public static readonly PreferenceType MaxTicketPrice = new(204, nameof(MaxTicketPrice));
    public static readonly PreferenceType PreferredVenues = new(205, nameof(PreferredVenues));

    public static readonly PreferenceType Limit = new(301, nameof(Limit));

    public static readonly PreferenceType Type = new(401, nameof(Type));

    public static readonly PreferenceType PreferredPlaceTypes = new(501, nameof(PreferredPlaceTypes));
    public static readonly PreferenceType SearchRadius = new(502, nameof(SearchRadius));
    public static readonly PreferenceType PreferredPriceLevel = new(503, nameof(PreferredPriceLevel));
    public static readonly PreferenceType PreferredTransportModes = new(504, nameof(PreferredTransportModes));
    public static readonly PreferenceType MaxWalkingDistance = new(505, nameof(MaxWalkingDistance));
    public static readonly PreferenceType PreferredOperators = new(506, nameof(PreferredOperators));
    public static readonly PreferenceType Ssrc = new(507, nameof(Ssrc));

    public static readonly PreferenceType NoTraceMode = new(803, nameof(NoTraceMode));
    public static readonly PreferenceType DeepSearch = new(804, nameof(DeepSearch));
}

