using TripRadar.Server.Application.Constants.LocalPlaces;

namespace TripRadar.Server.Application.UseCases.Common.Providers;

public class LocalPlacesColumnHierarchyProvider : ColumnHierarchyProvider
{
    protected override Dictionary<string, string?> ColumnHierarchies => new()
    {
        // Root level properties
        {
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata,
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters,
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters
        },
        { SavedLocalPlacesQueryColumnNameConstants.AdResults, SavedLocalPlacesQueryColumnNameConstants.AdResults },
        {
            SavedLocalPlacesQueryColumnNameConstants.LocalResults, SavedLocalPlacesQueryColumnNameConstants.LocalResults
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces,
            SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces
        },
        { SavedLocalPlacesQueryColumnNameConstants.Pagination, SavedLocalPlacesQueryColumnNameConstants.Pagination },
        {
            SavedLocalPlacesQueryColumnNameConstants.SerpApiPagination,
            SavedLocalPlacesQueryColumnNameConstants.SerpApiPagination
        },

        // Search metadata properties
        { SavedLocalPlacesQueryColumnNameConstants.Id, SavedLocalPlacesQueryColumnNameConstants.SearchMetadata },
        { SavedLocalPlacesQueryColumnNameConstants.Status, SavedLocalPlacesQueryColumnNameConstants.SearchMetadata },
        {
            SavedLocalPlacesQueryColumnNameConstants.JsonEndpoint,
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata
        },
        { SavedLocalPlacesQueryColumnNameConstants.CreatedAt, SavedLocalPlacesQueryColumnNameConstants.SearchMetadata },
        {
            SavedLocalPlacesQueryColumnNameConstants.ProcessedAt,
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.RawHtmlFile,
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.TotalTimeTaken,
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata
        },

        // Search parameters properties
        { SavedLocalPlacesQueryColumnNameConstants.Engine, SavedLocalPlacesQueryColumnNameConstants.SearchParameters },
        { SavedLocalPlacesQueryColumnNameConstants.Query, SavedLocalPlacesQueryColumnNameConstants.SearchParameters },
        {
            SavedLocalPlacesQueryColumnNameConstants.LocationRequested,
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.LocationUsed,
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.GoogleDomain,
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.LanguageCode,
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.CountryCode,
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters
        },
        { SavedLocalPlacesQueryColumnNameConstants.Device, SavedLocalPlacesQueryColumnNameConstants.SearchParameters },
        { SavedLocalPlacesQueryColumnNameConstants.Uule, SavedLocalPlacesQueryColumnNameConstants.SearchParameters },

        // Local result properties
        { SavedLocalPlacesQueryColumnNameConstants.Position, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Title, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Rating, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        {
            SavedLocalPlacesQueryColumnNameConstants.ReviewsOriginal,
            SavedLocalPlacesQueryColumnNameConstants.LocalResults
        },
        { SavedLocalPlacesQueryColumnNameConstants.Reviews, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Price, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Type, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Address, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Description, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.PlaceId, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        {
            SavedLocalPlacesQueryColumnNameConstants.PlaceIdSearch,
            SavedLocalPlacesQueryColumnNameConstants.LocalResults
        },
        { SavedLocalPlacesQueryColumnNameConstants.Lsig, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        { SavedLocalPlacesQueryColumnNameConstants.Thumbnail, SavedLocalPlacesQueryColumnNameConstants.LocalResults },
        {
            SavedLocalPlacesQueryColumnNameConstants.GpsCoordinates,
            SavedLocalPlacesQueryColumnNameConstants.LocalResults
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.ServiceOptions,
            SavedLocalPlacesQueryColumnNameConstants.LocalResults
        },

        // GPS coordinates properties
        { SavedLocalPlacesQueryColumnNameConstants.Latitude, SavedLocalPlacesQueryColumnNameConstants.GpsCoordinates },
        { SavedLocalPlacesQueryColumnNameConstants.Longitude, SavedLocalPlacesQueryColumnNameConstants.GpsCoordinates },

        // Service options properties
        { SavedLocalPlacesQueryColumnNameConstants.DineIn, SavedLocalPlacesQueryColumnNameConstants.ServiceOptions },
        { SavedLocalPlacesQueryColumnNameConstants.Takeout, SavedLocalPlacesQueryColumnNameConstants.ServiceOptions },
        { SavedLocalPlacesQueryColumnNameConstants.Delivery, SavedLocalPlacesQueryColumnNameConstants.ServiceOptions },
        {
            SavedLocalPlacesQueryColumnNameConstants.NoDelivery, SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.InStorePickup,
            SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.InStoreShopping,
            SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.CurbsidePickup,
            SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.NoContactDelivery,
            SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.Reservable, SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },
        {
            SavedLocalPlacesQueryColumnNameConstants.WheelchairAccessible,
            SavedLocalPlacesQueryColumnNameConstants.ServiceOptions
        },

        // Advertisement result properties
        { SavedLocalPlacesQueryColumnNameConstants.AdTitle, SavedLocalPlacesQueryColumnNameConstants.AdResults },
        { SavedLocalPlacesQueryColumnNameConstants.DisplayedLink, SavedLocalPlacesQueryColumnNameConstants.AdResults },
        { SavedLocalPlacesQueryColumnNameConstants.Hours, SavedLocalPlacesQueryColumnNameConstants.AdResults },

        // Discover more places properties
        {
            SavedLocalPlacesQueryColumnNameConstants.Places, SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces
        },
        { SavedLocalPlacesQueryColumnNameConstants.Link, SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces },
        {
            SavedLocalPlacesQueryColumnNameConstants.SerpApiLink,
            SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces
        },

        // Pagination properties
        { SavedLocalPlacesQueryColumnNameConstants.Current, SavedLocalPlacesQueryColumnNameConstants.Pagination },
        { SavedLocalPlacesQueryColumnNameConstants.Next, SavedLocalPlacesQueryColumnNameConstants.Pagination },
        { SavedLocalPlacesQueryColumnNameConstants.OtherPages, SavedLocalPlacesQueryColumnNameConstants.Pagination },
        {
            SavedLocalPlacesQueryColumnNameConstants.NextLink,
            SavedLocalPlacesQueryColumnNameConstants.SerpApiPagination
        }
    };

    protected override HashSet<string?> ValidColumns => [..ColumnHierarchies.Keys.Concat(ColumnHierarchies.Values)];
}
