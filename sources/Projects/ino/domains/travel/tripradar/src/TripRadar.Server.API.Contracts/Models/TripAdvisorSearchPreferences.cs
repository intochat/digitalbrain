using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class TripAdvisorSearchPreferences
{
    [JsonPropertyName("Ssrc")]
    [DataMember(Name = "Ssrc")]
    [RegularExpression("^[aArhgvf]$", ErrorMessage = "Ssrc must be one of: a, r, A, h, g, v, f.")]
    public string? Ssrc { get; set; }
}
