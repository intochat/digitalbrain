using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetCurrenciesResponse
{
    [JsonPropertyName("currencies")]
    [DataMember(Name = "currencies")]
    [Required]
    public IEnumerable<CurrencyResponse> Currencies { get; set; } = null!;
}
