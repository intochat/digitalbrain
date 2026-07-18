using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class FlightSearchQueryDTO
{
    [Preference(nameof(PreferenceType.PreferredDepartureAirportCode))]
    [JsonPropertyName("departureId")]
    public string? DepartureId { get; set; }

    [JsonPropertyName("arrivalId")]
    public string? ArrivalId { get; set; }
}
