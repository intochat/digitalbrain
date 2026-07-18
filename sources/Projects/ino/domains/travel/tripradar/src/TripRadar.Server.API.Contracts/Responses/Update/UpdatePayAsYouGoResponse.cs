using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Update;

public class UpdatePayAsYouGoResponse
{
    [JsonPropertyName("enabled")]
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; }
}
