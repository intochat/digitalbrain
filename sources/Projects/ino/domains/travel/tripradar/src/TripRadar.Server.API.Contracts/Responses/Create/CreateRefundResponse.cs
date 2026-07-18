using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreateRefundResponse
{
    [JsonPropertyName("refundId")]
    [DataMember(Name = "refundId")]
    public string RefundId { get; set; } = null!;

    [JsonPropertyName("paymentIntentId")]
    [DataMember(Name = "paymentIntentId")]
    public string PaymentIntentId { get; set; } = null!;

    [JsonPropertyName("amount")]
    [DataMember(Name = "amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    [DataMember(Name = "currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("status")]
    [DataMember(Name = "status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("reason")]
    [DataMember(Name = "reason")]
    public string Reason { get; set; } = null!;

    [JsonPropertyName("created")]
    [DataMember(Name = "created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("metadata")]
    [DataMember(Name = "metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
