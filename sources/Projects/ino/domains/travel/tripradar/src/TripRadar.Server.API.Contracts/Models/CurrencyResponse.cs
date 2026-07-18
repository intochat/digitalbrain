using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class CurrencyResponse
{
    [JsonPropertyName("currencyCode")]
    [DataMember(Name = "currencyCode")]
    [Required]
    public string CurrencyCode { get; set; } = null!;

    [JsonPropertyName("currencyName")]
    [DataMember(Name = "currencyName")]
    [Required]
    public string CurrencyName { get; set; } = null!;
}
