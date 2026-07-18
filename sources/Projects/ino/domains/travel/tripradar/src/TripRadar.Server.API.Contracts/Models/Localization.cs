using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class Localization
{
    [Required]
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Country code (Gl) must be exactly 2 characters.")]
    public string? Gl { get; set; } // Country code

    [Required]
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Language code (Hl) must be exactly 2 characters.")]
    public string? Hl { get; set; } // Language code

    [Required]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency code must be exactly 3 characters.")]
    public string? Currency { get; set; } = "USD"; // Currency code (e.g., USD)

    [StringLength(50, ErrorMessage = "Google Domain must not exceed 50 characters.")]
    public string? GoogleDomain { get; set; } // Google domain for localization (e.g., google.com, google.fr)
}
