using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Constants.Hotels;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Infrastructure.Filters;

public class HotelResponseFilter(ILogger<HotelResponseFilter> logger)
    : BaseSearchResponseFilter<GetHotelResponseDTO>(logger)
{
    private static readonly Dictionary<string, string> _propertyMappings = new()
    {
        { nameof(Property.Type), SavedHotelQueryColumnNameConstants.Type },
        { nameof(Property.Name), SavedHotelQueryColumnNameConstants.Name },
        { nameof(Property.Description), SavedHotelQueryColumnNameConstants.Description },
        { nameof(Property.Link), SavedHotelQueryColumnNameConstants.Link },
        { nameof(Property.PropertyToken), SavedHotelQueryColumnNameConstants.PropertyToken },
        {
            nameof(Property.SerpapiPropertyDetailsLink),
            SavedHotelQueryColumnNameConstants.SerpapiPropertyDetailsLink
        },
        { nameof(Property.GpsCoordinates), SavedHotelQueryColumnNameConstants.GpsCoordinates },
        { nameof(Property.CheckInTime), SavedHotelQueryColumnNameConstants.CheckInTime },
        { nameof(Property.CheckOutTime), SavedHotelQueryColumnNameConstants.CheckOutTime },
        { nameof(Property.RatePerNight), SavedHotelQueryColumnNameConstants.RatePerNight },
        { nameof(Property.TotalRate), SavedHotelQueryColumnNameConstants.TotalRate },
        { nameof(Property.Deal), SavedHotelQueryColumnNameConstants.Deal },
        { nameof(Property.DealDescription), SavedHotelQueryColumnNameConstants.DealDescription },
        { nameof(Property.NearbyPlaces), SavedHotelQueryColumnNameConstants.NearbyPlaces },
        { nameof(Property.HotelClass), SavedHotelQueryColumnNameConstants.HotelClass },
        { nameof(Property.ExtractedHotelClass), SavedHotelQueryColumnNameConstants.ExtractedHotelClass },
        { nameof(Property.Images), SavedHotelQueryColumnNameConstants.Images },
        { nameof(Property.OverallRating), SavedHotelQueryColumnNameConstants.OverallRating },
        { nameof(Property.Reviews), SavedHotelQueryColumnNameConstants.Reviews },
        { nameof(Property.Ratings), SavedHotelQueryColumnNameConstants.Ratings },
        { nameof(Property.LocationRating), SavedHotelQueryColumnNameConstants.LocationRating },
        { nameof(Property.ReviewsBreakdown), SavedHotelQueryColumnNameConstants.ReviewsBreakdown },
        { nameof(Property.Amenities), SavedHotelQueryColumnNameConstants.Amenities },
        { nameof(Property.ExcludedAmenities), SavedHotelQueryColumnNameConstants.ExcludedAmenities },
        { nameof(Property.EssentialInfo), SavedHotelQueryColumnNameConstants.EssentialInfo },
        { nameof(Property.EcoCertified), SavedHotelQueryColumnNameConstants.EcoCertified },
        { nameof(Property.Prices), SavedHotelQueryColumnNameConstants.Prices }
    };

    private static readonly Dictionary<string, string> _nearbyPlaceMappings = new()
    {
        { nameof(NearbyPlace.Name), SavedHotelQueryColumnNameConstants.PlaceName },
        { nameof(NearbyPlace.Transportations), SavedHotelQueryColumnNameConstants.Transportations }
    };

    private static readonly Dictionary<string, string> _transportationMappings = new()
    {
        { nameof(Transportation.Type), SavedHotelQueryColumnNameConstants.TransportationType },
        { nameof(Transportation.Duration), SavedHotelQueryColumnNameConstants.Duration }
    };

    private static readonly Dictionary<string, string> _imageMappings = new()
    {
        { nameof(Image.Thumbnail), SavedHotelQueryColumnNameConstants.Thumbnail },
        { nameof(Image.OriginalImage), SavedHotelQueryColumnNameConstants.OriginalImage }
    };

    private static readonly Dictionary<string, string> _ratingMappings = new()
    {
        { nameof(Rating.Stars), SavedHotelQueryColumnNameConstants.Stars },
        { nameof(Rating.Count), SavedHotelQueryColumnNameConstants.Count }
    };

    private static readonly Dictionary<string, string> _reviewBreakdownMappings = new()
    {
        { nameof(ReviewBreakdown.Name), SavedHotelQueryColumnNameConstants.ReviewName },
        { nameof(ReviewBreakdown.Description), SavedHotelQueryColumnNameConstants.ReviewDescription },
        { nameof(ReviewBreakdown.TotalMentioned), SavedHotelQueryColumnNameConstants.TotalMentioned },
        { nameof(ReviewBreakdown.Positive), SavedHotelQueryColumnNameConstants.Positive },
        { nameof(ReviewBreakdown.Negative), SavedHotelQueryColumnNameConstants.Negative },
        { nameof(ReviewBreakdown.Neutral), SavedHotelQueryColumnNameConstants.Neutral }
    };

    private static readonly Dictionary<string, string> _priceMappings = new()
    {
        { nameof(Price.Source), SavedHotelQueryColumnNameConstants.Source },
        { nameof(Price.Logo), SavedHotelQueryColumnNameConstants.Logo },
        { nameof(Price.NumGuests), SavedHotelQueryColumnNameConstants.NumGuests },
        { nameof(Price.RatePerNight), SavedHotelQueryColumnNameConstants.RatePerNight },
        { nameof(Price.FreeCancellation), SavedHotelQueryColumnNameConstants.FreeCancellation },
        { nameof(Price.FreeCancellationUntilDate), SavedHotelQueryColumnNameConstants.FreeCancellationUntilDate },
        { nameof(Price.FreeCancellationUntilTime), SavedHotelQueryColumnNameConstants.FreeCancellationUntilTime }
    };

    private static readonly Dictionary<string, string> _rateMappings = new()
    {
        { nameof(Rate.Lowest), SavedHotelQueryColumnNameConstants.Lowest },
        { nameof(Rate.ExtractedLowest), SavedHotelQueryColumnNameConstants.ExtractedLowest },
        { nameof(Rate.BeforeTaxesFees), SavedHotelQueryColumnNameConstants.BeforeTaxesFees },
        { nameof(Rate.ExtractedBeforeTaxesFees), SavedHotelQueryColumnNameConstants.ExtractedBeforeTaxesFees }
    };

    protected override GetHotelResponseDTO FilterResponse(GetHotelResponseDTO response, List<string> activeColumns)
    {
        var filteredResponse = new GetHotelResponseDTO();

        if (ShouldIncludeContainer(SavedHotelQueryColumnNameConstants.SearchMetadata, activeColumns))
            filteredResponse.SearchMetadata = response.SearchMetadata;

        if (ShouldIncludeContainer(SavedHotelQueryColumnNameConstants.SearchParameters, activeColumns))
            filteredResponse.SearchParameters = response.SearchParameters;

        if (ShouldIncludeContainer(SavedHotelQueryColumnNameConstants.SearchInformation, activeColumns))
            filteredResponse.SearchInformation = response.SearchInformation;

        if (ShouldIncludeContainer(SavedHotelQueryColumnNameConstants.Brands, activeColumns))
            filteredResponse.Brands = response.Brands;

        if (ShouldIncludeContainer(SavedHotelQueryColumnNameConstants.Properties, activeColumns) && response.Properties != null)
            filteredResponse.Properties = FilterProperties(response.Properties, activeColumns);

        if (ShouldIncludeContainer(SavedHotelQueryColumnNameConstants.SerpapiPagination, activeColumns))
            filteredResponse.SerpapiPagination = response.SerpapiPagination;

        return filteredResponse;
    }

    /// <summary>
    ///     Checks if a container should be included based on whether any of its child columns are active
    ///     or if the container itself is directly requested
    /// </summary>
    private static bool ShouldIncludeContainer(string containerName, List<string> activeColumns)
    {
        // Include if the container itself is directly requested
        if (IsColumnActive(containerName, activeColumns))
        {
            return true;
        }

        return containerName switch
        {
            SavedHotelQueryColumnNameConstants.Properties => activeColumns.Any(IsPropertyColumn),
            SavedHotelQueryColumnNameConstants.SearchMetadata => activeColumns.Any(IsSearchMetadataColumn),
            SavedHotelQueryColumnNameConstants.SearchParameters => activeColumns.Any(IsSearchParametersColumn),
            SavedHotelQueryColumnNameConstants.SearchInformation => activeColumns.Any(IsSearchInformationColumn),
            SavedHotelQueryColumnNameConstants.Brands => activeColumns.Any(IsBrandColumn),
            _ => false
        };
    }

    private static bool IsPropertyColumn(string columnName) =>
        columnName is SavedHotelQueryColumnNameConstants.Type or SavedHotelQueryColumnNameConstants.Name
            or SavedHotelQueryColumnNameConstants.Description or SavedHotelQueryColumnNameConstants.Link
            or SavedHotelQueryColumnNameConstants.PropertyToken
            or SavedHotelQueryColumnNameConstants.SerpapiPropertyDetailsLink
            or SavedHotelQueryColumnNameConstants.GpsCoordinates or SavedHotelQueryColumnNameConstants.CheckInTime
            or SavedHotelQueryColumnNameConstants.CheckOutTime or SavedHotelQueryColumnNameConstants.RatePerNight
            or SavedHotelQueryColumnNameConstants.TotalRate or SavedHotelQueryColumnNameConstants.Deal
            or SavedHotelQueryColumnNameConstants.DealDescription or SavedHotelQueryColumnNameConstants.NearbyPlaces
            or SavedHotelQueryColumnNameConstants.HotelClass or SavedHotelQueryColumnNameConstants.ExtractedHotelClass
            or SavedHotelQueryColumnNameConstants.Images or SavedHotelQueryColumnNameConstants.OverallRating
            or SavedHotelQueryColumnNameConstants.Reviews or SavedHotelQueryColumnNameConstants.Ratings
            or SavedHotelQueryColumnNameConstants.LocationRating or SavedHotelQueryColumnNameConstants.ReviewsBreakdown
            or SavedHotelQueryColumnNameConstants.Amenities or SavedHotelQueryColumnNameConstants.ExcludedAmenities
            or SavedHotelQueryColumnNameConstants.EssentialInfo or SavedHotelQueryColumnNameConstants.EcoCertified
            or SavedHotelQueryColumnNameConstants.Prices or SavedHotelQueryColumnNameConstants.Latitude
            or SavedHotelQueryColumnNameConstants.Longitude or SavedHotelQueryColumnNameConstants.Lowest
            or SavedHotelQueryColumnNameConstants.ExtractedLowest or SavedHotelQueryColumnNameConstants.BeforeTaxesFees
            or SavedHotelQueryColumnNameConstants.ExtractedBeforeTaxesFees
            or SavedHotelQueryColumnNameConstants.PlaceName or SavedHotelQueryColumnNameConstants.Transportations
            or SavedHotelQueryColumnNameConstants.TransportationType or SavedHotelQueryColumnNameConstants.Duration
            or SavedHotelQueryColumnNameConstants.Thumbnail or SavedHotelQueryColumnNameConstants.OriginalImage
            or SavedHotelQueryColumnNameConstants.Stars or SavedHotelQueryColumnNameConstants.Count
            or SavedHotelQueryColumnNameConstants.ReviewName or SavedHotelQueryColumnNameConstants.ReviewDescription
            or SavedHotelQueryColumnNameConstants.TotalMentioned or SavedHotelQueryColumnNameConstants.Positive
            or SavedHotelQueryColumnNameConstants.Negative or SavedHotelQueryColumnNameConstants.Neutral
            or SavedHotelQueryColumnNameConstants.Source or SavedHotelQueryColumnNameConstants.Logo
            or SavedHotelQueryColumnNameConstants.NumGuests or SavedHotelQueryColumnNameConstants.FreeCancellation
            or SavedHotelQueryColumnNameConstants.FreeCancellationUntilDate
            or SavedHotelQueryColumnNameConstants.FreeCancellationUntilTime;

    private static bool IsSearchMetadataColumn(string columnName) => false;

    private static bool IsSearchParametersColumn(string columnName) => false;

    private static bool IsSearchInformationColumn(string columnName) => columnName == SavedHotelQueryColumnNameConstants.TotalResults;

    private static bool IsBrandColumn(string columnName) => columnName is SavedHotelQueryColumnNameConstants.BrandId or SavedHotelQueryColumnNameConstants.BrandName or SavedHotelQueryColumnNameConstants.BrandChildren;

    private static List<Property> FilterProperties(List<Property> properties, List<string> activeColumns) =>
        properties.Select(property =>
        {
            var filteredProperty = CreateFilteredInstance(property, activeColumns, _propertyMappings);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.GpsCoordinates, activeColumns))
                filteredProperty.GpsCoordinates = FilterGpsCoordinates(property.GpsCoordinates, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.RatePerNight, activeColumns))
                filteredProperty.RatePerNight = FilterRate(property.RatePerNight, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.TotalRate, activeColumns))
                filteredProperty.TotalRate = FilterRate(property.TotalRate, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.NearbyPlaces, activeColumns))
                filteredProperty.NearbyPlaces = FilterNearbyPlaces(property.NearbyPlaces, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.Images, activeColumns))
                filteredProperty.Images = FilterImages(property.Images, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.Ratings, activeColumns))
                filteredProperty.Ratings = FilterRatings(property.Ratings, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.ReviewsBreakdown, activeColumns))
                filteredProperty.ReviewsBreakdown = FilterReviewsBreakdown(property.ReviewsBreakdown, activeColumns);

            if (IsColumnActive(SavedHotelQueryColumnNameConstants.Prices, activeColumns))
                filteredProperty.Prices = FilterPrices(property.Prices, activeColumns);

            return filteredProperty;
        }).ToList();

    private static GpsCoordinates? FilterGpsCoordinates(GpsCoordinates? coordinates, List<string> activeColumns)
    {
        if (coordinates == null)
            return null;

        return CreateFilteredInstance(coordinates, activeColumns,
            new Dictionary<string, string>
            {
                { nameof(GpsCoordinates.Latitude), SavedHotelQueryColumnNameConstants.Latitude },
                { nameof(GpsCoordinates.Longitude), SavedHotelQueryColumnNameConstants.Longitude }
            });
    }

    private static Rate? FilterRate(Rate? rate, List<string> activeColumns) =>
        rate == null ? null : CreateFilteredInstance(rate, activeColumns, _rateMappings);

    private static List<NearbyPlace>? FilterNearbyPlaces(List<NearbyPlace>? places, List<string> activeColumns) =>
        places?.Select(place =>
        {
            var filteredPlace = CreateFilteredInstance(place, activeColumns, _nearbyPlaceMappings);
            if (IsColumnActive(SavedHotelQueryColumnNameConstants.Transportations, activeColumns))
                filteredPlace.Transportations = FilterTransportations(place.Transportations, activeColumns);
            return filteredPlace;
        }).ToList();

    private static List<Transportation>? FilterTransportations(List<Transportation>? transportations, List<string> activeColumns) =>
        transportations?.Select(t => CreateFilteredInstance(t, activeColumns, _transportationMappings)).ToList();

    private static List<Image>? FilterImages(List<Image>? images, List<string> activeColumns) => images?.Select(image => CreateFilteredInstance(image, activeColumns, _imageMappings)).ToList();

    private static List<Rating>? FilterRatings(List<Rating>? ratings, List<string> activeColumns) => ratings?.Select(rating => CreateFilteredInstance(rating, activeColumns, _ratingMappings)).ToList();

    private static List<ReviewBreakdown>? FilterReviewsBreakdown(List<ReviewBreakdown>? breakdowns,
        List<string> activeColumns) =>
        breakdowns
            ?.Select(breakdown => CreateFilteredInstance(breakdown, activeColumns, _reviewBreakdownMappings)).ToList();

    private static List<Price>? FilterPrices(List<Price>? prices, List<string> activeColumns) =>
        prices?.Select(price =>
        {
            var filteredPrice = CreateFilteredInstance(price, activeColumns, _priceMappings);
            if (IsColumnActive(SavedHotelQueryColumnNameConstants.RatePerNight, activeColumns))
            {
                filteredPrice.RatePerNight = FilterRate(price.RatePerNight, activeColumns);
            }

            return filteredPrice;
        }).ToList();
}
