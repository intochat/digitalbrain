using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PaymentMethodsDTO
{
    [JsonPropertyName("paymentMethods")]
    public List<PaymentMethodItemDTO> PaymentMethods { get; set; } = [];
    [JsonPropertyName("hasActiveSubscription")]
    public bool HasActiveSubscription { get; set; }
}
