using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class EventPreferences
{
    [JsonPropertyName("Currency")]
    [DataMember(Name = "Currency")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-character currency code.")]
    public string? Currency { get; set; }

    [JsonPropertyName("Language")]
    [DataMember(Name = "Language")]
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Language must be a 2-character language code.")]
    public string? Language { get; set; }

    [JsonPropertyName("NoTraceMode")]
    [DataMember(Name = "NoTraceMode")]
    public bool? NoTraceMode { get; set; }
}
