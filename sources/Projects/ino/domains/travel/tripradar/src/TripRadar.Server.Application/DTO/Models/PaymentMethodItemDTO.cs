using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PaymentMethodItemDTO
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("card")]
    public CardDTO Card { get; set; } = null!;
    [JsonPropertyName("billingDetails")]
    public BillingDTO? BillingDetails { get; set; }
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
