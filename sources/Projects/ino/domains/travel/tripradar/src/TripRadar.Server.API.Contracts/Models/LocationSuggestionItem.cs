using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class LocationSuggestionItem
{
    [JsonPropertyName("locationId")]
    [DataMember(Name = "locationId")]
    [Required]
    public int LocationId { get; set; }

    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    [Required]
    public string Name { get; set; } = null!;

    [JsonPropertyName("canonicalName")]
    [DataMember(Name = "canonicalName")]
    [Required]
    public string CanonicalName { get; set; } = null!;

    [JsonPropertyName("countryCode")]
    [DataMember(Name = "countryCode")]
    [Required]
    public string CountryCode { get; set; } = null!;

    [JsonPropertyName("targetType")]
    [DataMember(Name = "targetType")]
    [Required]
    public string TargetType { get; set; } = null!;

    [JsonPropertyName("latitude")]
    [DataMember(Name = "latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    [DataMember(Name = "longitude")]
    public double? Longitude { get; set; }
}
