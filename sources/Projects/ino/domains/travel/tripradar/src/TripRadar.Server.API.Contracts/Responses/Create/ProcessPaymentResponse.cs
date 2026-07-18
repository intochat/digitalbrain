using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class ProcessPaymentResponse
{
    [JsonPropertyName("success")]
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    [DataMember(Name = "message")]
    public string Message { get; set; } = null!;

    [JsonPropertyName("paymentIntentId")]
    [DataMember(Name = "paymentIntentId")]
    public string PaymentIntentId { get; set; } = null!;
}
