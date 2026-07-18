using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public record RecurringDTO(
    [property: JsonPropertyName("interval")]
    string Interval,
    [property: JsonPropertyName("interval_count")]
    int IntervalCount
);
