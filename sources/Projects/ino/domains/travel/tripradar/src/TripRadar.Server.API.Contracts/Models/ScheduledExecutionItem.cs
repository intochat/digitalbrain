using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class ScheduledExecutionItem
{
    [JsonPropertyName("scheduledExecutionUniqueId")]
    [DataMember(Name = "scheduledExecutionUniqueId")]
    [Required]
    public Guid ScheduledExecutionUniqueId { get; set; }

    [JsonPropertyName("serviceType")]
    [DataMember(Name = "serviceType")]
    [Required]
    public string ServiceType { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    [Required]
    public bool IsActive { get; set; }

    [JsonPropertyName("nextExecutionTime")]
    [DataMember(Name = "nextExecutionTime")]
    [Required]
    public DateTime NextExecutionTime { get; set; }

    [JsonPropertyName("schedule")]
    [DataMember(Name = "schedule")]
    [Required]
    public string Schedule { get; set; } = string.Empty;

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    [Required]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("updatedOn")]
    [DataMember(Name = "updatedOn")]
    public DateTime? UpdatedOn { get; set; }

    [JsonPropertyName("requestSummary")]
    [DataMember(Name = "requestSummary")]
    [Required]
    public string RequestSummary { get; set; } = string.Empty;

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

    [JsonPropertyName("checkInDate")]
    [DataMember(Name = "checkInDate")]
    public DateTime? CheckInDate { get; set; }

    [JsonPropertyName("checkOutDate")]
    [DataMember(Name = "checkOutDate")]
    public DateTime? CheckOutDate { get; set; }

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("additionalParameters")]
    [DataMember(Name = "additionalParameters")]
    public string? AdditionalParameters { get; set; }

    [JsonPropertyName("selectedColumns")]
    [DataMember(Name = "selectedColumns")]
    public IList<QueryColumn>? SelectedColumns { get; set; }
}
