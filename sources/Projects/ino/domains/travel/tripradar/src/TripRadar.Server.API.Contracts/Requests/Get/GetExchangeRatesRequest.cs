using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetExchangeRatesRequest
{
    [JsonPropertyName("tripVaultName")]
    [DataMember(Name = "tripVaultName")]
    public string? TripVaultName { get; set; }

    [JsonPropertyName("baseCurrency")]
    [DataMember(Name = "baseCurrency")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Base currency is required.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Base currency must be a 3-character currency code.")]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Base currency must be a valid 3-letter currency code in uppercase.")]
    public required string BaseCurrency { get; set; }
}
