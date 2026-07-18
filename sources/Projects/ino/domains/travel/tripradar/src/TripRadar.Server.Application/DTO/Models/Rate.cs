using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class Rate
{
    [JsonPropertyName("lowest")]
    public string? Lowest { get; set; }

    [JsonPropertyName("extracted_lowest")]
    public decimal ExtractedLowest { get; set; }

    [JsonPropertyName("before_taxes_fees")]
    public string? BeforeTaxesFees { get; set; }

    [JsonPropertyName("extracted_before_taxes_fees")]
    public decimal ExtractedBeforeTaxesFees { get; set; }
}
