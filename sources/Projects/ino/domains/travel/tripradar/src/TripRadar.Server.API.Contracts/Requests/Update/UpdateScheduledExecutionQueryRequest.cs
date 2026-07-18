using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public sealed class UpdateScheduledExecutionQueryRequest
{
    [JsonPropertyName("searchQuery")]
    [DataMember(Name = "searchQuery")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("location")]
    [DataMember(Name = "location")]
    public string? Location { get; set; }

    [JsonPropertyName("radius")]
    [DataMember(Name = "radius")]
    public int? Radius { get; set; }

    [JsonPropertyName("departureAirportCode")]
    [DataMember(Name = "departureAirportCode")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Departure airport code must be exactly 3 characters.")]
    public string? DepartureAirportCode { get; set; }

    [JsonPropertyName("destinationAirportCode")]
    [DataMember(Name = "destinationAirportCode")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Destination airport code must be exactly 3 characters.")]
    public string? DestinationAirportCode { get; set; }

    [JsonPropertyName("departureDate")]
    [DataMember(Name = "departureDate")]
    [DataType(DataType.Date)]
    public DateTime? DepartureDate { get; set; }

    [JsonPropertyName("returnDate")]
    [DataMember(Name = "returnDate")]
    [DataType(DataType.Date)]
    public DateTime? ReturnDate { get; set; }

    [JsonPropertyName("checkInDate")]
    [DataMember(Name = "checkInDate")]
    [DataType(DataType.Date)]
    public DateTime? CheckInDate { get; set; }

    [JsonPropertyName("checkOutDate")]
    [DataMember(Name = "checkOutDate")]
    [DataType(DataType.Date)]
    public DateTime? CheckOutDate { get; set; }

    [JsonPropertyName("additionalParameters")]
    [DataMember(Name = "additionalParameters")]
    public Dictionary<string, object>? AdditionalParameters { get; set; }

    [JsonPropertyName("selectedColumns")]
    [DataMember(Name = "selectedColumns")]
    public IList<QueryColumn>? SelectedColumns { get; set; }
}
