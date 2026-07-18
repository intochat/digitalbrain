using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class PaymentStatusResponse
{
    [JsonPropertyName("paymentIntentId")]
    [DataMember(Name = "paymentIntentId")]
    public string PaymentIntentId { get; set; } = null!;

    [JsonPropertyName("status")]
    [DataMember(Name = "status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("amount")]
    [DataMember(Name = "amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    [DataMember(Name = "currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("fromTier")]
    [DataMember(Name = "fromTier")]
    public string FromTier { get; set; } = null!;

    [JsonPropertyName("toTier")]
    [DataMember(Name = "toTier")]
    public string ToTier { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    [DataMember(Name = "createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    [DataMember(Name = "updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
