using System.Text.Json.Serialization;
using System.Collections;
using System.Globalization;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetHotelRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    public required SearchQuery SearchQuery { get; set; } = new();

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    public required HotelAdvancedParameters AdvancedParameters { get; set; }

    [JsonPropertyName("filters")]
    public HotelAdvancedFilters? Filters { get; set; }

    [JsonPropertyName("vacationRentalsFilters")]
    public VacationRentalsFilters? VacationRentalsFilters { get; set; }

    [JsonPropertyName("nextPage")]
    public Pagination? NextPage { get; set; }

    [JsonPropertyName("booking")]
    public HotelBooking? Booking { get; set; }

    [Preference(nameof(PreferenceType.NoTraceMode))]
    [JsonPropertyName("zeroTrace")]
    public bool? ZeroTrace { get; set; }

    public Hashtable GetQueryParams()
    {
        var hashtable = new Hashtable
        {
            { "q", SearchQuery.Q },
            { "engine", "google_hotels" },
            { "check_in_date", AdvancedParameters.CheckInDate },
            { "check_out_date", AdvancedParameters.CheckOutDate },
            { "adults", AdvancedParameters.Adults?.ToString(CultureInfo.InvariantCulture) },
            { "children", AdvancedParameters.Children?.ToString(CultureInfo.InvariantCulture) },
            { "currency", Localization?.Currency },
            { "hl", Localization?.Hl },
            { "gl", Localization?.Gl },
            { "price_min", Filters?.MinPrice?.ToString(CultureInfo.InvariantCulture) },
            { "price_max", Filters?.MaxPrice?.ToString(CultureInfo.InvariantCulture) },
            { "free_cancellation", Filters?.FreeCancellation?.ToString().ToLowerInvariant() },
            { "special_offers", Filters?.SpecialOffers?.ToString().ToLowerInvariant() },
            { "eco_certified", Filters?.EcoCertified?.ToString().ToLowerInvariant() },
            { "vacation_rentals", VacationRentalsFilters?.VacationRentals?.ToString().ToLowerInvariant() },
            { "bedrooms", VacationRentalsFilters?.Bedrooms?.ToString(CultureInfo.InvariantCulture) },
            { "bathrooms", VacationRentalsFilters?.Bathrooms?.ToString(CultureInfo.InvariantCulture) },
            {
                "sort_by",
                Filters?.SortBy != null ? ((int)Filters.SortBy).ToString(CultureInfo.InvariantCulture) : null
            },
            {
                "rating",
                Filters?.Rating != null ? ((int)Filters.Rating).ToString(CultureInfo.InvariantCulture) : null
            },
            { "brands", Filters?.Brands },
            { "hotel_class", Filters?.HotelClass },
            { "start", NextPage?.Start?.ToString(CultureInfo.InvariantCulture) },
            { "property_token", Booking?.PropertyToken }
        };

        if (ZeroTrace == true)
        {
            hashtable["zero_trace"] = "true";
        }

        if (AdvancedParameters.ChildrenAges != null)
        {
            var childrenAges = string.Join(",", AdvancedParameters.ChildrenAges);
            hashtable.Add("children_ages", childrenAges);
        }

        if (Filters?.PropertyTypes != null)
        {
            var propertyTypeValues = new List<int>();
            foreach (var propertyType in Filters.PropertyTypes)
            {
                if (propertyType is HotelsPropertyType hotelPropertyType)
                {
                    propertyTypeValues.Add((int)hotelPropertyType);
                }
                else if (propertyType is VacationRentalPropertyType vacationPropertyType)
                {
                    propertyTypeValues.Add((int)vacationPropertyType);
                }
                else if (propertyType is string propertyTypeString)
                {
                    if (Enum.TryParse<HotelsPropertyType>(propertyTypeString, out var hotelType))
                    {
                        propertyTypeValues.Add((int)hotelType);
                    }
                    else if (Enum.TryParse<VacationRentalPropertyType>(propertyTypeString, out var vacationType))
                    {
                        propertyTypeValues.Add((int)vacationType);
                    }
                }
            }

            if (propertyTypeValues.Count > 0)
            {
                hashtable.Add("property_types", string.Join(",", propertyTypeValues));
            }
        }

        if (Filters?.Amenities != null)
        {
            var amenityValues = new List<int>();
            foreach (var amenity in Filters.Amenities)
            {
                if (amenity is HotelAmenityType hotelAmenity)
                {
                    amenityValues.Add((int)hotelAmenity);
                }
                else if (amenity is VacationRentalAmenityType vacationAmenity)
                {
                    amenityValues.Add((int)vacationAmenity);
                }
                else if (amenity is string amenityString)
                {
                    // Handle string enum names from GraphQL
                    if (Enum.TryParse<HotelAmenityType>(amenityString, out var hotelAmenityType))
                    {
                        amenityValues.Add((int)hotelAmenityType);
                    }
                    else if (Enum.TryParse<VacationRentalAmenityType>(amenityString, out var vacationAmenityType))
                    {
                        amenityValues.Add((int)vacationAmenityType);
                    }
                }
            }

            if (amenityValues.Count > 0)
            {
                hashtable.Add("amenities", string.Join(",", amenityValues));
            }
        }

        return hashtable;
    }
}

