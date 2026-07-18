namespace TripRadar.Server.Application.DTO.Models;

public class SubscriptionCheckoutDto
{
    public string ClientSecret { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public decimal AmountSubtotal { get; set; }

    public decimal AmountDiscount { get; set; }

    public decimal AmountTotal { get; set; }

    public string? PromoCode { get; set; }
}