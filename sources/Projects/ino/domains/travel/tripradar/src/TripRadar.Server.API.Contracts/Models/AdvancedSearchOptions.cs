using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Models;

public class AdvancedSearchOptions
{
    public FlightType? Type { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Outbound date must be in the format YYYY-MM-DD.")]
    public string? OutboundDate { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Return date must be in the format YYYY-MM-DD.")]
    public string? ReturnDate { get; set; }

    public TravelClassType? TravelClass { get; set; }

    public List<MultiCityLeg>? MultiCityJson { get; set; }

    public bool? ShowHidden { get; set; }

    public bool? DeepSearch { get; set; }
}
