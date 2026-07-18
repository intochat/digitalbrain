using System.Text.Json.Serialization;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.DTO.Models;

public class Payment
{
    [JsonPropertyName("tierName")]
    public string TierName { get; set; } = null!;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("billingPeriodName")]
    public string BillingPeriodName { get; set; } = null!;

    [JsonPropertyName("currencyName")]
    public string CurrencyName { get; set; } = null!;

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = null!;

    [JsonPropertyName("tokensPerMonthLimit")]
    public decimal TokensPerMonthLimit { get; set; }

    public static Payment MapToDto(Domain.Aggregates.Price price)
    {
        return new Payment
        {
            TierName = price.Tier.Name,
            Amount = price.Amount / 100m,
            BillingPeriodName = price.BillingPeriod.Name,
            CurrencyName = price.Currency.CurrencyName,
            CurrencyCode = price.Currency.CurrencyCode,
            TokensPerMonthLimit = price.Tier.TokensPerMonthLimit
        };
    }
}
