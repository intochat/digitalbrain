using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Enums;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetHotelRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required] public required SearchQuery SearchQuery { get; set; } = new();

    public Localization? Localization { get; set; }

    [Required] public required HotelAdvancedParameters AdvancedParameters { get; set; }

    public HotelAdvancedFilters? Filters { get; set; }

    public VacationRentalsFilters? VacationRentalsFilters { get; set; }

    public TokenPagination? TokenPagination { get; set; }

    public HotelBooking? Booking { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(AdvancedParameters.CheckInDate) ||
            string.IsNullOrWhiteSpace(AdvancedParameters.CheckOutDate))
        {
            yield break;
        }

        if (Filters != null && VacationRentalsFilters?.VacationRentals == true)
        {
            yield return new ValidationResult(
                "Only one of 'Filters' or 'VacationRentalsFilters' can be set depending on the property type.",
                [nameof(Filters), nameof(VacationRentalsFilters)]);
        }

        if (DateTime.TryParse(AdvancedParameters.CheckInDate, out var checkIn) &&
            DateTime.TryParse(AdvancedParameters.CheckOutDate, out var checkOut))
        {
            if (checkIn > checkOut)
            {
                yield return new ValidationResult("Check-in date cannot be later than check-out date.",
                    [nameof(AdvancedParameters.CheckInDate), nameof(AdvancedParameters.CheckOutDate)]);
            }
        }

        if (VacationRentalsFilters?.VacationRentals != true && VacationRentalsFilters != null)
        {
            yield return new ValidationResult(
                "Vacation Rentals filters must only be used for Vacation Rentals searches.",
                [nameof(VacationRentalsFilters)]);
        }

        if (VacationRentalsFilters?.VacationRentals == true && Filters != null)
        {
            yield return new ValidationResult("Hotel filters must not be set when searching for Vacation Rentals.",
                [nameof(Filters)]);
        }

        if (VacationRentalsFilters?.VacationRentals == true && Booking?.PropertyToken == null)
        {
            yield return new ValidationResult("PropertyToken is required for Vacation Rentals bookings.",
                [nameof(Booking.PropertyToken)]);
        }

        var isVacationRental = VacationRentalsFilters?.VacationRentals == true;

        if (Filters?.PropertyTypes != null)
        {
            foreach (var propertyType in Filters.PropertyTypes)
            {
                switch (isVacationRental)
                {
                    case true when propertyType is not VacationRentalPropertyType:
                        yield return new ValidationResult(
                            "When VacationRentals is true, PropertyTypes must only contain VacationRentalPropertyType values.",
                            [nameof(Filters.PropertyTypes)]);
                        break;
                    case false when propertyType is not HotelsPropertyType:
                        yield return new ValidationResult(
                            "When VacationRentals is false, PropertyTypes must only contain HotelsPropertyTypes values.",
                            [nameof(Filters.PropertyTypes)]);
                        break;
                }
            }
        }

        if (Filters?.Amenities != null)
        {
            foreach (var amenity in Filters.Amenities)
            {
                switch (isVacationRental)
                {
                    case true when amenity is not VacationRentalAmenityType:
                        yield return new ValidationResult(
                            "When VacationRentals is true, Amenities must contain only VacationRentalAmenityType values.",
                            [nameof(Filters.Amenities)]);
                        break;
                    case false when amenity is not HotelAmenityType:
                        yield return new ValidationResult(
                            "When VacationRentals is false, Amenities must contain only HotelAmenityType values.",
                            [nameof(Filters.Amenities)]);
                        break;
                }
            }
        }
    }
}

