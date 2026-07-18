using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateGoogleLoginRequest
{
    [JsonPropertyName("id_token")]
    [DataMember(Name = "id_token")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Google ID token is required.")]
    public string IdToken { get; set; } = null!;
}
