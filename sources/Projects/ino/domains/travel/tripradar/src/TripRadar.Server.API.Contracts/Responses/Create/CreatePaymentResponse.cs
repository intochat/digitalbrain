using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreatePaymentResponse
{
    [JsonPropertyName("clientSecret")]
    [DataMember(Name = "clientSecret")]
    public string ClientSecret { get; set; } = null!;

    [JsonPropertyName("paymentIntentId")]
    [DataMember(Name = "paymentIntentId")]
    public string PaymentIntentId { get; set; } = null!;
}
