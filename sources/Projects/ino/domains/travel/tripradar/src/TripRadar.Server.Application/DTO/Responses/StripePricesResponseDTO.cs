using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public record StripePricesResponseDTO(
    [property: JsonPropertyName("prices")] IEnumerable<StripePriceDTO> Prices
);
