using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class BillingPeriodResponse
{
    [JsonPropertyName("start")]
    [DataMember(Name = "start")]
    public DateTime Start { get; set; }

    [JsonPropertyName("end")]
    [DataMember(Name = "end")]
    public DateTime End { get; set; }

    [JsonPropertyName("daysRemaining")]
    [DataMember(Name = "daysRemaining")]
    public int DaysRemaining { get; set; }
}
