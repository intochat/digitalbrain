using TripRadar.Server.API.Contracts.Enums;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using HotelAdvancedFilters = TripRadar.Server.API.Contracts.Models.HotelAdvancedFilters;
using HotelAdvancedParameters = TripRadar.Server.API.Contracts.Models.HotelAdvancedParameters;
using HotelBooking = TripRadar.Server.API.Contracts.Models.HotelBooking;
using HotelRatingFilterType = TripRadar.Server.API.Contracts.Enums.HotelRatingFilterType;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using TokenPagination = TripRadar.Server.API.Contracts.Models.TokenPagination;
using SearchQuery = TripRadar.Server.API.Contracts.Models.SearchQuery;
using VacationRentalAmenityType = TripRadar.Server.API.Contracts.Enums.VacationRentalAmenityType;
using VacationRentalPropertyType = TripRadar.Server.API.Contracts.Enums.VacationRentalPropertyType;
using VacationRentalsFilters = TripRadar.Server.API.Contracts.Models.VacationRentalsFilters;

namespace TripRadar.Server.API.GraphQL.Types;

public class HotelType : ObjectType<GetHotelsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetHotelsResponse> descriptor)
    {
        descriptor.Description("Response containing hotel search results and related metadata.");

        descriptor.Field(f => f.SearchMetadata)
            .Description("Metadata about the search request and response.");

        descriptor.Field(f => f.SearchParameters)
            .Description("Parameters used in the hotel search request.");

        descriptor.Field(f => f.SearchInformation)
            .Description("Information about the search results like total number of properties.");

        descriptor.Field(f => f.Brands)
            .Description("List of hotel brands found in the search results.");

        descriptor.Field(f => f.Properties)
            .Description("List of hotel properties matching the search criteria.");

        descriptor.Field(f => f.SerpapiPagination)
            .Description("Pagination information for retrieving additional results.");
    }
}

public class HotelDataInputType : InputObjectType<GetHotelRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetHotelRequest> descriptor)
    {
        descriptor.Name("GetHotelRequest");
        descriptor.Description("Input parameters for hotel search requests.");

        descriptor.Field(f => f.SearchQuery)
            .Type<NonNullType<HotelSearchQueryInputType>>()
            .Description("Basic search parameters for finding hotels.");

        descriptor.Field(f => f.Localization)
            .Type<LocalizationSettingsInputType>()
            .Description("Localization settings for language, currency, and region preferences.");

        descriptor.Field(f => f.AdvancedParameters)
            .Type<NonNullType<HotelAdvancedParametersInputType>>()
            .Description("Required parameters for dates and guest information.");

        descriptor.Field(f => f.Filters)
            .Type<HotelAdvancedFiltersInputType>()
            .Description("Optional filters to refine hotel search results.");

        descriptor.Field(f => f.VacationRentalsFilters)
            .Type<VacationRentalsFiltersInputType>()
            .Description("Optional filters specific to vacation rentals.");

        descriptor.Field(f => f.TokenPagination)
            .Type<HotelPaginationInputType>()
            .Description("Pagination parameters for retrieving additional results.");

        descriptor.Field(f => f.Booking)
            .Type<HotelBookingInputType>()
            .Description("Optional booking parameters for specific property information.");
    }
}

public class HotelSearchQueryInputType : InputObjectType<SearchQuery>
{
    protected override void Configure(IInputObjectTypeDescriptor<SearchQuery> descriptor)
    {
        descriptor.Name("HotelSearchQuery");
        descriptor.Description("Basic search query parameters for hotels.");

        descriptor.Field(f => f.Q)
            .Type<NonNullType<StringType>>()
            .Description("The location to search for hotels. Can be a city, address, or point of interest.");
    }
}

public class LocalizationSettingsInputType : InputObjectType<Localization>
{
    protected override void Configure(IInputObjectTypeDescriptor<Localization> descriptor)
    {
        descriptor.Name("LocalizationSettings");
        descriptor.Description("Localization settings for the hotel search.");

        descriptor.Field(f => f.Currency)
            .Type<StringType>()
            .Description("The currency code for displaying prices (e.g., USD, EUR, GBP).");

        descriptor.Field(f => f.Hl)
            .Type<StringType>()
            .Description("The interface language code (e.g., en for English, fr for French).");

        descriptor.Field(f => f.Gl)
            .Type<StringType>()
            .Description("The country code for geographic localization (e.g., us, uk, ca).");
    }
}

public class HotelAdvancedParametersInputType : InputObjectType<HotelAdvancedParameters>
{
    protected override void Configure(IInputObjectTypeDescriptor<HotelAdvancedParameters> descriptor)
    {
        descriptor.Name("HotelAdvancedParameters");
        descriptor.Description("Advanced parameters for hotel search including dates and guests.");

        descriptor.Field(f => f.CheckInDate)
            .Type<NonNullType<StringType>>()
            .Description("Check-in date in the format YYYY-MM-DD.");

        descriptor.Field(f => f.CheckOutDate)
            .Type<NonNullType<StringType>>()
            .Description("Check-out date in the format YYYY-MM-DD.");

        descriptor.Field(f => f.Adults)
            .Type<IntType>()
            .Description("Number of adults (1-20). Defaults to 1 if not specified.");

        descriptor.Field(f => f.Children)
            .Type<IntType>()
            .Description("Number of children (0-10). Defaults to 0 if not specified.");

        descriptor.Field(f => f.ChildrenAges)
            .Type<ListType<IntType>>()
            .Description("Ages of children, required when children count is greater than 0.");
    }
}

public class HotelAdvancedFiltersInputType : InputObjectType<HotelAdvancedFilters>
{
    protected override void Configure(IInputObjectTypeDescriptor<HotelAdvancedFilters> descriptor)
    {
        descriptor.Name("HotelAdvancedFilters");
        descriptor.Description("Advanced filters for refining hotel search results.");

        descriptor.Field(f => f.SortBy)
            .Type<HotelSortByTypeEnum>()
            .Description("Sort order for the search results (e.g., by price, rating, or reviews).");

        descriptor.Field(f => f.MinPrice)
            .Type<IntType>()
            .Description("Minimum price per night in the specified currency.");

        descriptor.Field(f => f.MaxPrice)
            .Type<IntType>()
            .Description("Maximum price per night in the specified currency.");

        descriptor.Field(f => f.PropertyTypes)
            .Type<ListType<StringType>>()
            .Description(
                "Types of properties to include in search results. Valid hotel types: BeachHotels, BoutiqueHotels, Hostels, Inns, Motels, Resorts, SpaHotels, BedAndBreakfasts, Other, ApartmentHotels, Minshuku, JapaneseStyleBusinessHotels, Ryokan. Valid vacation rental types: Apartments, Bungalows, Cabins, Chalets, Cottages, Gites, HolidayVillages, Houses, Houseboats, Villas, Other, ApartmentHotels.");

        descriptor.Field(f => f.Amenities)
            .Type<ListType<StringType>>()
            .Description(
                "Amenities that properties must have. Valid hotel amenities: FreeParking, Parking, IndoorPool, OutdoorPool, Pool, FitnessCenter, Restaurant, FreeBreakfast, Spa, BeachAccess, ChildFriendly, Bar, PetFriendly, RoomService, FreeWiFi, AirConditioned, AllInclusiveAvailable, WheelchairAccessible, EVCharger. Valid vacation rental amenities: HotTub, AirConditioned, OutdoorGrill, Fireplace, PatioOrDeck, Kitchen, FitnessCentre, Cot, BeachAccess, ChildFriendly, PetFriendly, FreeWiFi, Pool.");

        descriptor.Field(f => f.Rating)
            .Type<HotelRatingFilterTypeEnum>()
            .Description("Minimum guest rating for properties (e.g., 3.5+, 4.0+, 4.5+).");

        descriptor.Field(f => f.Brands)
            .Type<StringType>()
            .Description("Comma-separated list of hotel brand IDs to filter by.");

        descriptor.Field(f => f.HotelClass)
            .Type<StringType>()
            .Description("Hotel class/star rating to filter by (e.g., '3', '4', '5').");

        descriptor.Field(f => f.FreeCancellation)
            .Type<BooleanType>()
            .Description("When true, only shows properties with free cancellation options.");

        descriptor.Field(f => f.SpecialOffers)
            .Type<BooleanType>()
            .Description("When true, only shows properties with special offers or deals.");

        descriptor.Field(f => f.EcoCertified)
            .Type<BooleanType>()
            .Description("When true, only shows properties with eco-friendly certifications.");
    }
}

public class VacationRentalsFiltersInputType : InputObjectType<VacationRentalsFilters>
{
    protected override void Configure(IInputObjectTypeDescriptor<VacationRentalsFilters> descriptor)
    {
        descriptor.Name("VacationRentalsFilters");
        descriptor.Description("Filters specific to vacation rental properties.");

        descriptor.Field(f => f.VacationRentals)
            .Type<BooleanType>()
            .Description("When true, search for vacation rentals instead of hotels.");

        descriptor.Field(f => f.Bedrooms)
            .Type<IntType>()
            .Description("Minimum number of bedrooms required in vacation rental properties.");

        descriptor.Field(f => f.Bathrooms)
            .Type<IntType>()
            .Description("Minimum number of bathrooms required in vacation rental properties.");
    }
}

public class HotelPaginationInputType : InputObjectType<TokenPagination>
{
    protected override void Configure(IInputObjectTypeDescriptor<TokenPagination> descriptor)
    {
        descriptor.Name("HotelPagination");
        descriptor.Description("Pagination parameters for hotel search results.");

        descriptor.Field(f => f.NextPageToken)
            .Type<StringType>()
            .Description("Token for retrieving the next page of results.");
    }
}

public class HotelBookingInputType : InputObjectType<HotelBooking>
{
    protected override void Configure(IInputObjectTypeDescriptor<HotelBooking> descriptor)
    {
        descriptor.Name("HotelBooking");
        descriptor.Description("Parameters for retrieving detailed booking information for a specific property.");

        descriptor.Field(f => f.PropertyToken)
            .Type<StringType>()
            .Description("Token identifying a specific property for detailed booking information.");
    }
}

public class HotelSortByTypeEnum : EnumType<HotelSortByType>
{
    protected override void Configure(IEnumTypeDescriptor<HotelSortByType> descriptor)
    {
        descriptor.Name("HotelSortByType");
        descriptor.Description("Sort options for hotel search results.");

        descriptor.Value(HotelSortByType.LowestPrice)
            .Name("LowestPrice")
            .Description("Sort by lowest price first.");

        descriptor.Value(HotelSortByType.HighestRating)
            .Name("HighestRating")
            .Description("Sort by highest guest rating first.");

        descriptor.Value(HotelSortByType.MostReviewed)
            .Name("MostReviewed")
            .Description("Sort by most reviewed properties first.");
    }
}

public class HotelRatingFilterTypeEnum : EnumType<HotelRatingFilterType>
{
    protected override void Configure(IEnumTypeDescriptor<HotelRatingFilterType> descriptor)
    {
        descriptor.Name("HotelRatingFilterType");
        descriptor.Description("Filter options for minimum guest ratings.");

        descriptor.Value(HotelRatingFilterType.Rating35Plus)
            .Name("Rating35Plus")
            .Description("Properties with ratings of 3.5 or higher.");

        descriptor.Value(HotelRatingFilterType.Rating40Plus)
            .Name("Rating40Plus")
            .Description("Properties with ratings of 4.0 or higher.");

        descriptor.Value(HotelRatingFilterType.Rating45Plus)
            .Name("Rating45Plus")
            .Description("Properties with ratings of 4.5 or higher.");
    }
}

public class HotelsPropertyTypeEnum : EnumType<HotelsPropertyType>
{
    protected override void Configure(IEnumTypeDescriptor<HotelsPropertyType> descriptor)
    {
        descriptor.Name("HotelsPropertyType");
        descriptor.Description("Types of hotel properties available for booking.");

        descriptor.Value(HotelsPropertyType.BeachHotels).Name("BeachHotels");
        descriptor.Value(HotelsPropertyType.BoutiqueHotels).Name("BoutiqueHotels");
        descriptor.Value(HotelsPropertyType.Hostels).Name("Hostels");
        descriptor.Value(HotelsPropertyType.Inns).Name("Inns");
        descriptor.Value(HotelsPropertyType.Motels).Name("Motels");
        descriptor.Value(HotelsPropertyType.Resorts).Name("Resorts");
        descriptor.Value(HotelsPropertyType.SpaHotels).Name("SpaHotels");
        descriptor.Value(HotelsPropertyType.BedAndBreakfasts).Name("BedAndBreakfasts");
        descriptor.Value(HotelsPropertyType.Other).Name("Other");
        descriptor.Value(HotelsPropertyType.ApartmentHotels).Name("ApartmentHotels");
        descriptor.Value(HotelsPropertyType.Minshuku).Name("Minshuku");
        descriptor.Value(HotelsPropertyType.JapaneseStyleBusinessHotels).Name("JapaneseStyleBusinessHotels");
        descriptor.Value(HotelsPropertyType.Ryokan).Name("Ryokan");
    }
}

public class HotelAmenityTypeEnum : EnumType<HotelAmenityType>
{
    protected override void Configure(IEnumTypeDescriptor<HotelAmenityType> descriptor)
    {
        descriptor.Name("HotelAmenityType");
        descriptor.Description("Available amenities in hotel properties.");

        descriptor.Value(HotelAmenityType.FreeParking).Name("FreeParking");
        descriptor.Value(HotelAmenityType.Parking).Name("Parking");
        descriptor.Value(HotelAmenityType.IndoorPool).Name("IndoorPool");
        descriptor.Value(HotelAmenityType.OutdoorPool).Name("OutdoorPool");
        descriptor.Value(HotelAmenityType.Pool).Name("Pool");
        descriptor.Value(HotelAmenityType.FitnessCenter).Name("FitnessCenter");
        descriptor.Value(HotelAmenityType.Restaurant).Name("Restaurant");
        descriptor.Value(HotelAmenityType.FreeBreakfast).Name("FreeBreakfast");
        descriptor.Value(HotelAmenityType.Spa).Name("Spa");
        descriptor.Value(HotelAmenityType.BeachAccess).Name("BeachAccess");
        descriptor.Value(HotelAmenityType.ChildFriendly).Name("ChildFriendly");
        descriptor.Value(HotelAmenityType.Bar).Name("Bar");
        descriptor.Value(HotelAmenityType.PetFriendly).Name("PetFriendly");
        descriptor.Value(HotelAmenityType.RoomService).Name("RoomService");
        descriptor.Value(HotelAmenityType.FreeWiFi).Name("FreeWiFi");
        descriptor.Value(HotelAmenityType.AirConditioned).Name("AirConditioned");
        descriptor.Value(HotelAmenityType.AllInclusiveAvailable).Name("AllInclusiveAvailable");
        descriptor.Value(HotelAmenityType.WheelchairAccessible).Name("WheelchairAccessible");
        descriptor.Value(HotelAmenityType.EVCharger).Name("EVCharger");
    }
}

public class VacationRentalPropertyTypeEnum : EnumType<VacationRentalPropertyType>
{
    protected override void Configure(IEnumTypeDescriptor<VacationRentalPropertyType> descriptor)
    {
        descriptor.Name("VacationRentalPropertyType");
        descriptor.Description("Types of vacation rental properties available for booking.");

        descriptor.Value(VacationRentalPropertyType.Apartments).Name("Apartments");
        descriptor.Value(VacationRentalPropertyType.Bungalows).Name("Bungalows");
        descriptor.Value(VacationRentalPropertyType.Cabins).Name("Cabins");
        descriptor.Value(VacationRentalPropertyType.Chalets).Name("Chalets");
        descriptor.Value(VacationRentalPropertyType.Cottages).Name("Cottages");
        descriptor.Value(VacationRentalPropertyType.Gites).Name("Gites");
        descriptor.Value(VacationRentalPropertyType.HolidayVillages).Name("HolidayVillages");
        descriptor.Value(VacationRentalPropertyType.Houses).Name("Houses");
        descriptor.Value(VacationRentalPropertyType.Houseboats).Name("Houseboats");
        descriptor.Value(VacationRentalPropertyType.Villas).Name("Villas");
        descriptor.Value(VacationRentalPropertyType.Other).Name("Other");
        descriptor.Value(VacationRentalPropertyType.ApartmentHotels).Name("ApartmentHotels");
    }
}

public class VacationRentalAmenityTypeEnum : EnumType<VacationRentalAmenityType>
{
    protected override void Configure(IEnumTypeDescriptor<VacationRentalAmenityType> descriptor)
    {
        descriptor.Name("VacationRentalAmenityType");
        descriptor.Description("Available amenities in vacation rental properties.");

        descriptor.Value(VacationRentalAmenityType.HotTub).Name("HotTub");
        descriptor.Value(VacationRentalAmenityType.AirConditioned).Name("AirConditioned");
        descriptor.Value(VacationRentalAmenityType.OutdoorGrill).Name("OutdoorGrill");
        descriptor.Value(VacationRentalAmenityType.Fireplace).Name("Fireplace");
        descriptor.Value(VacationRentalAmenityType.PatioOrDeck).Name("PatioOrDeck");
        descriptor.Value(VacationRentalAmenityType.Kitchen).Name("Kitchen");
        descriptor.Value(VacationRentalAmenityType.FitnessCentre).Name("FitnessCentre");
        descriptor.Value(VacationRentalAmenityType.Cot).Name("Cot");
        descriptor.Value(VacationRentalAmenityType.BeachAccess).Name("BeachAccess");
        descriptor.Value(VacationRentalAmenityType.ChildFriendly).Name("ChildFriendly");
        descriptor.Value(VacationRentalAmenityType.PetFriendly).Name("PetFriendly");
        descriptor.Value(VacationRentalAmenityType.FreeWiFi).Name("FreeWiFi");
        descriptor.Value(VacationRentalAmenityType.Pool).Name("Pool");
    }
}

