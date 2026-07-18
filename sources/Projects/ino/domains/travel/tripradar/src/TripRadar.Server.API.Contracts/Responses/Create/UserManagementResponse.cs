using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class UserManagementResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
