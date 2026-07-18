using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class MultiCityLeg
{
    [StringLength(10, ErrorMessage = "DepartureId must be at most 10 characters.")]
    public required string DepartureId { get; set; }

    [StringLength(10, ErrorMessage = "ArrivalId must be at most 10 characters.")]
    public required string ArrivalId { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Date must be in the format YYYY-MM-DD.")]
    public required string Date { get; set; }

    [RegularExpression(@"^(\d{1,2},\d{1,2})(,\d{1,2},\d{1,2})?$", ErrorMessage = "Times must be comma-separated hours (0-23). Format: 'start,end' (e.g., '6,12' for 6am-12pm) or 'outbound_start,outbound_end,return_start,return_end'.")]
    public required string Times { get; set; }
}
