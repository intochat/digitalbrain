using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetLanguagesResponse
{
    [JsonPropertyName("languages")]
    [DataMember(Name = "languages")]
    [Required]
    public IEnumerable<LanguageResponse> Languages { get; set; } = null!;
}