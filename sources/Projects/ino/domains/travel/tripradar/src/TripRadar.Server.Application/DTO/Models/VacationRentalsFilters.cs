using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class VacationRentalsFilters
{
    [JsonPropertyName("vacationRentals")]
    public bool? VacationRentals { get; set; }

    [JsonPropertyName("bedrooms")]
    public int? Bedrooms { get; set; }

    [JsonPropertyName("bathrooms")]
    public int? Bathrooms { get; set; }
}
