using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.Comms.Core.Errors;

[DataContract]
[Serializable]
public class ErrorResponse
{
    [DataMember(Name = "errorCode")]
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = null!;

    [DataMember(Name = "errorReason")]
    [JsonPropertyName("errorReason")]
    public string ErrorReason { get; set; } = null!;

    [DataMember(Name = "details")]
    [JsonPropertyName("details")]
    public IList<string> Details { get; set; } = [];
}
