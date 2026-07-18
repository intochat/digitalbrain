using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class ProcessPaymentRequest
{
    [JsonPropertyName("paymentIntentId")]
    [DataMember(Name = "paymentIntentId")]
    [Required]
    public string PaymentIntentId { get; set; } = null!;
}
