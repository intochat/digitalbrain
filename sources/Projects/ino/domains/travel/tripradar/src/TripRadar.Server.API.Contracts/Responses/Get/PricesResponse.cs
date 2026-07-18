using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class PricesResponse
{
    [JsonPropertyName("prices")]
    [DataMember(Name = "prices")]
    public List<PriceResponse> Prices { get; set; } = [];
}