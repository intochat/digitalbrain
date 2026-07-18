using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class FlightSearchQuery
{
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Airport code(s) must be between 3 and 100 characters.")]
    [RegularExpression(@"^[A-Z]{3}(,[A-Z]{3})*$", ErrorMessage = "Must be one or more comma-separated 3-letter IATA codes in uppercase.")]
    public string? DepartureId { get; set; }

    [StringLength(100, MinimumLength = 3, ErrorMessage = "Airport code(s) must be between 3 and 100 characters.")]
    [RegularExpression(@"^[A-Z]{3}(,[A-Z]{3})*$", ErrorMessage = "Must be one or more comma-separated 3-letter IATA codes in uppercase.")]
    public string? ArrivalId { get; set; }
}
