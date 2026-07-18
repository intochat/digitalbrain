using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class HotelAdvancedParameters : IValidatableObject
{
    [Required(ErrorMessage = "Check-in date is required.")]
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Check-in date must be in the format YYYY-MM-DD.")]
    public required string CheckInDate { get; set; }

    [Required(ErrorMessage = "Check-out date is required.")]
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Check-out date must be in the format YYYY-MM-DD.")]
    public required string CheckOutDate { get; set; }

    [Range(1, 20, ErrorMessage = "At least one adult is required.")]
    public int? Adults { get; set; }

    [Range(0, 10)] public int? Children { get; set; }

    public IEnumerable<int>? ChildrenAges { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateTime.TryParse(CheckInDate, out var checkIn) && DateTime.TryParse(CheckOutDate, out var checkOut))
        {
            if (checkIn > checkOut)
            {
                yield return new ValidationResult("Check-in date cannot be later than check-out date.",
                    [nameof(CheckInDate), nameof(CheckOutDate)]);
            }
        }

        if (Children > 0 && (ChildrenAges == null || ChildrenAges.Count() != Children))
        {
            yield return new ValidationResult(
                $"The number of child ages ({ChildrenAges?.Count() ?? 0}) must match the number of children ({Children}).",
                [nameof(Children), nameof(ChildrenAges)]);
        }
    }
}
