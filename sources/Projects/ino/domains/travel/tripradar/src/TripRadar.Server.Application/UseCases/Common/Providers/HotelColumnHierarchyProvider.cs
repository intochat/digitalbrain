using TripRadar.Server.Application.Constants.Hotels;

namespace TripRadar.Server.Application.UseCases.Common.Providers;

public class HotelColumnHierarchyProvider : ColumnHierarchyProvider
{
    protected override Dictionary<string, string?> ColumnHierarchies => new()
    {
        // Root level properties
        { SavedHotelQueryColumnNameConstants.SearchMetadata, SavedHotelQueryColumnNameConstants.SearchMetadata },
        { SavedHotelQueryColumnNameConstants.SearchParameters, SavedHotelQueryColumnNameConstants.SearchParameters },
        { SavedHotelQueryColumnNameConstants.SearchInformation, SavedHotelQueryColumnNameConstants.SearchInformation },
        { SavedHotelQueryColumnNameConstants.Brands, SavedHotelQueryColumnNameConstants.Brands },
        { SavedHotelQueryColumnNameConstants.Properties, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.SerpapiPagination, SavedHotelQueryColumnNameConstants.SerpapiPagination },

        // Property properties
        { SavedHotelQueryColumnNameConstants.Type, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Name, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Description, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Link, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.PropertyToken, SavedHotelQueryColumnNameConstants.Properties },
        {
            SavedHotelQueryColumnNameConstants.SerpapiPropertyDetailsLink, SavedHotelQueryColumnNameConstants.Properties
        },
        { SavedHotelQueryColumnNameConstants.GpsCoordinates, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.CheckInTime, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.CheckOutTime, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.RatePerNight, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.TotalRate, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Deal, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.DealDescription, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.NearbyPlaces, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.HotelClass, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.ExtractedHotelClass, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Images, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.OverallRating, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Reviews, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Ratings, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.LocationRating, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.ReviewsBreakdown, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Amenities, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.ExcludedAmenities, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.EssentialInfo, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.EcoCertified, SavedHotelQueryColumnNameConstants.Properties },
        { SavedHotelQueryColumnNameConstants.Prices, SavedHotelQueryColumnNameConstants.Properties }
    };

    protected override HashSet<string?> ValidColumns => [..ColumnHierarchies.Keys.Concat(ColumnHierarchies.Values)];
}
