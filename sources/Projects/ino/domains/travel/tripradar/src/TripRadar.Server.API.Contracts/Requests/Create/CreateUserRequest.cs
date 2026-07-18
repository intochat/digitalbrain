using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateUserRequest
{
    [JsonPropertyName("password")]
    [DataMember(Name = "password")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Password is required.")]
    [IsPasswordValid]
    [Obfuscated]
    public string Password { get; set; } = null!;

    [JsonPropertyName("email")]
    [DataMember(Name = "email")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [Obfuscated]
    public string Email { get; set; } = null!;

    [JsonPropertyName("firstName")]
    [DataMember(Name = "firstName")]
    [StringLength(ValidationConstants.MaxNameLength, ErrorMessage = "The maximum length of first name is 64 characters.")]
    [Obfuscated]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    [DataMember(Name = "lastName")]
    [StringLength(ValidationConstants.MaxNameLength, ErrorMessage = "The maximum length of last name is 64 characters.")]
    [Obfuscated]
    public string? LastName { get; set; }

    [JsonPropertyName("phoneNumber")]
    [DataMember(Name = "phoneNumber")]
    [IsPhoneNumberValid]
    [Obfuscated]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("hasDataStorageConsent")]
    [DataMember(Name = "hasDataStorageConsent")]
    [Required(ErrorMessage = "Data storage consent is required.")]
    public bool HasDataStorageConsent { get; set; }
}
