using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class StripeBillingPeriodInfo
{
    [JsonPropertyName("start")]
    public DateTime Start { get; set; }

    [JsonPropertyName("end")]
    public DateTime End { get; set; }

    [JsonPropertyName("daysRemaining")]
    public int DaysRemaining { get; set; }
}
