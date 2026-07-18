using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public record StripePriceDTO(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("created")]
    long Created,
    [property: JsonPropertyName("currency")]
    string Currency,
    [property: JsonPropertyName("livemode")]
    bool Livemode,
    [property: JsonPropertyName("nickname")]
    string? Nickname,
    [property: JsonPropertyName("product")]
    string ProductId,
    [property: JsonPropertyName("recurring")]
    RecurringDTO? Recurring,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("unit_amount")]
    long UnitAmount
);
