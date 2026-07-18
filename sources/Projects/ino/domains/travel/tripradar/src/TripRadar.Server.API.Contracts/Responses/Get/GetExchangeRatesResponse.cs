using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetExchangeRatesResponse
{
    [JsonPropertyName("baseCurrency")]
    [DataMember(Name = "baseCurrency")]
    public string BaseCurrency { get; set; } = null!;

    [JsonPropertyName("rates")]
    [DataMember(Name = "rates")]
    public Dictionary<string, decimal> Rates { get; set; } = new();

    [JsonPropertyName("exchangeDate")]
    [DataMember(Name = "exchangeDate")]
    public DateTime ExchangeDate { get; set; } = DateTime.UtcNow;
}
