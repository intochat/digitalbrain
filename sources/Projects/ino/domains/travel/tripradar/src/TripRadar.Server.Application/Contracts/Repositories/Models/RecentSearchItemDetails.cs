using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Repositories.Models;

public abstract class RecentSearchPayloadDetails;

public sealed class FlightRecentSearchPayloadDetails : RecentSearchPayloadDetails
{
    public string? DepartureAirportCode { get; set; }
    public string? DepartureAirportCity { get; set; }
    public string? DestinationAirportCode { get; set; }
    public string? DestinationAirportCity { get; set; }
    public DateTime? DepartureDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public int? Adults { get; set; }
    public int? Children { get; set; }
    public string? TravelClass { get; set; }
    public string? SortBy { get; set; }
    public int? MaxPrice { get; set; }
    public int? Stops { get; set; }
    public IList<string> IncludeAirlines { get; set; } = [];
    public int? Bags { get; set; }
    public string? OutboundTimes { get; set; }
    public string? ReturnTimes { get; set; }
    public bool? EmissionsOnly { get; set; }
}

public sealed class HotelRecentSearchPayloadDetails : RecentSearchPayloadDetails
{
    public string? Location { get; set; }
    public DateTime? CheckInDate { get; set; }
    public DateTime? CheckOutDate { get; set; }
    public int? Adults { get; set; }
    public int? Children { get; set; }
    public string? SortBy { get; set; }
    public int? MaxPrice { get; set; }
}

public sealed class RecentSearchItemDetails
{
    public Guid UniqueId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;
    public DateTime CreatedOn { get; set; }
    public RecentSearchPayloadDetails Payload { get; set; } = null!;
}
