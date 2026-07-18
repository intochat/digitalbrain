using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class VacationRentalsFilters
{
    public bool? VacationRentals { get; set; }

    [Range(0, 1000, ErrorMessage = "Bedrooms must be greater than 0.")]
    public int? Bedrooms { get; set; }

    [Range(0, 1000, ErrorMessage = "Bathrooms must be greater than 0.")]
    public int? Bathrooms { get; set; }
}
