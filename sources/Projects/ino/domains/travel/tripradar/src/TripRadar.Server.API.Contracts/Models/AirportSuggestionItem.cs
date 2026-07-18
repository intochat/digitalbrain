using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class AirportSuggestionItem
{
    [JsonPropertyName("code")]
    [DataMember(Name = "code")]
    [Required]
    public string Code { get; set; } = null!;

    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    [Required]
    public string Name { get; set; } = null!;

    [JsonPropertyName("city")]
    [DataMember(Name = "city")]
    [Required]
    public string City { get; set; } = null!;

    [JsonPropertyName("countryCode")]
    [DataMember(Name = "countryCode")]
    [Required]
    public string CountryCode { get; set; } = null!;

    [JsonPropertyName("distanceFromCenter")]
    [DataMember(Name = "distanceFromCenter")]
    public int? DistanceFromCenter { get; set; }

    [JsonPropertyName("searchAliases")]
    [DataMember(Name = "searchAliases")]
    public string? SearchAliases { get; set; }
}
