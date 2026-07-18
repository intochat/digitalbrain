using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class Localization
{
    [Preference(nameof(PreferenceType.Currency))]
    [JsonPropertyName("currency")]
    public string? Currency { get; set; } // Currency code (e.g., USD)

    [JsonPropertyName("hl")]
    public string? Hl { get; set; } // Language code

    [JsonPropertyName("gl")]
    public string? Gl { get; set; } // Country code

    [JsonPropertyName("domain")]
    public string? Domain { get; set; } // Google domain (e.g., google.com, google.co.uk)
}
