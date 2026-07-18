using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Models;

public class HotelAdvancedFilters : IValidatableObject
{
    public HotelSortByType? SortBy { get; set; }

    public int? MinPrice { get; set; }

    public int? MaxPrice { get; set; }

    public IEnumerable<object>? PropertyTypes { get; set; }

    [JsonIgnore]
    public IEnumerable<HotelsPropertyType>? HotelPropertyTypes => PropertyTypes?.OfType<HotelsPropertyType>();

    [JsonIgnore]
    public IEnumerable<VacationRentalPropertyType>? VacationRentalPropertyTypes =>
        PropertyTypes?.OfType<VacationRentalPropertyType>();

    public IEnumerable<object>? Amenities { get; set; }

    [JsonIgnore] public IEnumerable<HotelAmenityType>? HotelAmenities => Amenities?.OfType<HotelAmenityType>();

    [JsonIgnore]
    public IEnumerable<VacationRentalAmenityType>? VacationRentalAmenities =>
        Amenities?.OfType<VacationRentalAmenityType>();

    public HotelRatingFilterType? Rating { get; set; }

    public string? Brands { get; set; }

    public string? HotelClass { get; set; }

    public bool? FreeCancellation { get; set; }

    public bool? SpecialOffers { get; set; }

    public bool? EcoCertified { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
        {
            yield return new ValidationResult("Minimum price cannot be greater than maximum price.",
                [nameof(MinPrice), nameof(MaxPrice)]);
        }
    }
}
