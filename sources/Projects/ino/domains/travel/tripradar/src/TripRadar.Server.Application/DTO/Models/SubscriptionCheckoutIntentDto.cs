namespace TripRadar.Server.Application.DTO.Models;

public class SubscriptionCheckoutIntentDto
{
    public string? ClientSecret { get; set; }

    public long AmountSubtotal { get; set; }

    public long AmountDiscount { get; set; }

    public long AmountTotal { get; set; }

    public string Currency { get; set; } = string.Empty;
}