using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetYelpSearchRequest
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Location is required.")]
    [StringLength(200, ErrorMessage = "Location must not exceed 200 characters.")]
    [JsonPropertyName("find_loc")]
    public required string FindLoc { get; set; }

    [StringLength(200, ErrorMessage = "Description must not exceed 200 characters.")]
    [JsonPropertyName("find_desc")]
    public string? FindDesc { get; set; }

    [StringLength(100, ErrorMessage = "Yelp domain must not exceed 100 characters.")]
    [JsonPropertyName("yelp_domain")]
    public string? YelpDomain { get; set; }

    [StringLength(50, ErrorMessage = "Sortby must not exceed 50 characters.")]
    [JsonPropertyName("sortby")]
    public string? SortBy { get; set; }

    [StringLength(200, ErrorMessage = "Attrs must not exceed 200 characters.")]
    [JsonPropertyName("attrs")]
    public string? Attrs { get; set; }

    [StringLength(200, ErrorMessage = "Cflt must not exceed 200 characters.")]
    [JsonPropertyName("cflt")]
    public string? Cflt { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Start must be zero or a positive number.")]
    [JsonPropertyName("start")]
    public int? Start { get; set; }
}

