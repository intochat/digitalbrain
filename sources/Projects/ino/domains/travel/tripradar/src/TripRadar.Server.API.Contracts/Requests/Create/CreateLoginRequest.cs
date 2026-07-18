using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateLoginRequest
{
    [JsonPropertyName("usernameOrEmail")]
    [DataMember(Name = "usernameOrEmail")]
    [StringLength(ValidationConstants.MaxUsernameLength,
        ErrorMessage = "The maximum length is 50 characters.")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Username or email is required.")]
    [IsUsernameOrEmailValid]
    public string UsernameOrEmail { get; set; } = null!;

    [JsonPropertyName("password")]
    [DataMember(Name = "password")]
    [IsPasswordValid]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Password is required.")]
    [Obfuscated]
    public string Password { get; set; } = null!;
}
