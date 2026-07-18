using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FlightRecentSearchPayloadResponse), typeDiscriminator: "Flight")]
[JsonDerivedType(typeof(HotelRecentSearchPayloadResponse), typeDiscriminator: "Hotel")]
public abstract class RecentSearchPayloadResponse;

public sealed class FlightRecentSearchPayloadResponse : RecentSearchPayloadResponse
{
    [JsonPropertyName("departureAirportCode")]
    [DataMember(Name = "departureAirportCode")]
    public string? DepartureAirportCode { get; set; }

    [JsonPropertyName("departureAirportCity")]
    [DataMember(Name = "departureAirportCity")]
    public string? DepartureAirportCity { get; set; }

    [JsonPropertyName("destinationAirportCode")]
    [DataMember(Name = "destinationAirportCode")]
    public string? DestinationAirportCode { get; set; }

    [JsonPropertyName("destinationAirportCity")]
    [DataMember(Name = "destinationAirportCity")]
    public string? DestinationAirportCity { get; set; }

    [JsonPropertyName("departureDate")]
    [DataMember(Name = "departureDate")]
    public DateTime? DepartureDate { get; set; }

    [JsonPropertyName("returnDate")]
    [DataMember(Name = "returnDate")]
    public DateTime? ReturnDate { get; set; }

    [JsonPropertyName("adults")]
    [DataMember(Name = "adults")]
    public int? Adults { get; set; }

    [JsonPropertyName("children")]
    [DataMember(Name = "children")]
    public int? Children { get; set; }

    [JsonPropertyName("travelClass")]
    [DataMember(Name = "travelClass")]
    public string? TravelClass { get; set; }

    [JsonPropertyName("sortBy")]
    [DataMember(Name = "sortBy")]
    public string? SortBy { get; set; }

    [JsonPropertyName("maxPrice")]
    [DataMember(Name = "maxPrice")]
    public int? MaxPrice { get; set; }

    [JsonPropertyName("stops")]
    [DataMember(Name = "stops")]
    public int? Stops { get; set; }

    [JsonPropertyName("includeAirlines")]
    [DataMember(Name = "includeAirlines")]
    public IList<string> IncludeAirlines { get; set; } = [];

    [JsonPropertyName("bags")]
    [DataMember(Name = "bags")]
    public int? Bags { get; set; }

    [JsonPropertyName("outboundTimes")]
    [DataMember(Name = "outboundTimes")]
    public string? OutboundTimes { get; set; }

    [JsonPropertyName("returnTimes")]
    [DataMember(Name = "returnTimes")]
    public string? ReturnTimes { get; set; }

    [JsonPropertyName("emissionsOnly")]
    [DataMember(Name = "emissionsOnly")]
    public bool? EmissionsOnly { get; set; }
}

public sealed class HotelRecentSearchPayloadResponse : RecentSearchPayloadResponse
{
    [JsonPropertyName("location")]
    [DataMember(Name = "location")]
    public string? Location { get; set; }

    [JsonPropertyName("checkInDate")]
    [DataMember(Name = "checkInDate")]
    public DateTime? CheckInDate { get; set; }

    [JsonPropertyName("checkOutDate")]
    [DataMember(Name = "checkOutDate")]
    public DateTime? CheckOutDate { get; set; }

    [JsonPropertyName("adults")]
    [DataMember(Name = "adults")]
    public int? Adults { get; set; }

    [JsonPropertyName("children")]
    [DataMember(Name = "children")]
    public int? Children { get; set; }

    [JsonPropertyName("sortBy")]
    [DataMember(Name = "sortBy")]
    public string? SortBy { get; set; }

    [JsonPropertyName("maxPrice")]
    [DataMember(Name = "maxPrice")]
    public int? MaxPrice { get; set; }
}

public sealed class RecentSearchItemResponse
{
    [JsonPropertyName("uniqueId")]
    [DataMember(Name = "uniqueId")]
    public Guid UniqueId { get; set; }

    [JsonPropertyName("serviceType")]
    [DataMember(Name = "serviceType")]
    public ServiceType ServiceType { get; set; }

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("payload")]
    [DataMember(Name = "payload")]
    public RecentSearchPayloadResponse Payload { get; set; } = null!;
}
