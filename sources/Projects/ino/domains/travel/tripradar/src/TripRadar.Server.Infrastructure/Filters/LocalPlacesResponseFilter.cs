using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Constants.LocalPlaces;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Infrastructure.Filters;

public class LocalPlacesResponseFilter(ILogger<LocalPlacesResponseFilter> logger) : BaseSearchResponseFilter<GetLocalPlacesResponseDTO>(logger)
{
    private static readonly Dictionary<string, string> _localResultMappings = new()
    {
        { nameof(LocalPlaceResultDTO.Position), SavedLocalPlacesQueryColumnNameConstants.Position },
        { nameof(LocalPlaceResultDTO.Title), SavedLocalPlacesQueryColumnNameConstants.Title },
        { nameof(LocalPlaceResultDTO.Rating), SavedLocalPlacesQueryColumnNameConstants.Rating },
        { nameof(LocalPlaceResultDTO.ReviewsOriginal), SavedLocalPlacesQueryColumnNameConstants.ReviewsOriginal },
        { nameof(LocalPlaceResultDTO.Reviews), SavedLocalPlacesQueryColumnNameConstants.Reviews },
        { nameof(LocalPlaceResultDTO.Price), SavedLocalPlacesQueryColumnNameConstants.Price },
        { nameof(LocalPlaceResultDTO.Type), SavedLocalPlacesQueryColumnNameConstants.Type },
        { nameof(LocalPlaceResultDTO.Address), SavedLocalPlacesQueryColumnNameConstants.Address },
        { nameof(LocalPlaceResultDTO.Description), SavedLocalPlacesQueryColumnNameConstants.Description },
        { nameof(LocalPlaceResultDTO.PlaceId), SavedLocalPlacesQueryColumnNameConstants.PlaceId },
        { nameof(LocalPlaceResultDTO.PlaceIdSearch), SavedLocalPlacesQueryColumnNameConstants.PlaceIdSearch },
        { nameof(LocalPlaceResultDTO.ProviderId), SavedLocalPlacesQueryColumnNameConstants.ProviderId },
        { nameof(LocalPlaceResultDTO.Lsig), SavedLocalPlacesQueryColumnNameConstants.Lsig },
        { nameof(LocalPlaceResultDTO.Thumbnail), SavedLocalPlacesQueryColumnNameConstants.Thumbnail },
        { nameof(LocalPlaceResultDTO.Images), SavedLocalPlacesQueryColumnNameConstants.Images },
        { nameof(LocalPlaceResultDTO.GpsCoordinates), SavedLocalPlacesQueryColumnNameConstants.GpsCoordinates },
        { nameof(LocalPlaceResultDTO.ServiceOptions), SavedLocalPlacesQueryColumnNameConstants.ServiceOptions },
        { nameof(LocalPlaceResultDTO.Phone), SavedLocalPlacesQueryColumnNameConstants.Phone },
        { nameof(LocalPlaceResultDTO.Hours), SavedLocalPlacesQueryColumnNameConstants.Hours },
        { nameof(LocalPlaceResultDTO.Extensions), SavedLocalPlacesQueryColumnNameConstants.Extensions },
        { nameof(LocalPlaceResultDTO.Links), SavedLocalPlacesQueryColumnNameConstants.Links }
    };

    private static readonly Dictionary<string, string> _adResultMappings = new()
    {
        { nameof(LocalAdvertisementResultDTO.Position), SavedLocalPlacesQueryColumnNameConstants.Position },
        { nameof(LocalAdvertisementResultDTO.AdTitle), SavedLocalPlacesQueryColumnNameConstants.AdTitle },
        { nameof(LocalAdvertisementResultDTO.DisplayedLink), SavedLocalPlacesQueryColumnNameConstants.DisplayedLink },
        { nameof(LocalAdvertisementResultDTO.Title), SavedLocalPlacesQueryColumnNameConstants.Title },
        { nameof(LocalAdvertisementResultDTO.Type), SavedLocalPlacesQueryColumnNameConstants.Type },
        { nameof(LocalAdvertisementResultDTO.ReviewsOriginal), SavedLocalPlacesQueryColumnNameConstants.ReviewsOriginal },
        { nameof(LocalAdvertisementResultDTO.Reviews), SavedLocalPlacesQueryColumnNameConstants.Reviews },
        { nameof(LocalAdvertisementResultDTO.Rating), SavedLocalPlacesQueryColumnNameConstants.Rating },
        { nameof(LocalAdvertisementResultDTO.Address), SavedLocalPlacesQueryColumnNameConstants.Address },
        { nameof(LocalAdvertisementResultDTO.Hours), SavedLocalPlacesQueryColumnNameConstants.Hours },
        { nameof(LocalAdvertisementResultDTO.PlaceId), SavedLocalPlacesQueryColumnNameConstants.PlaceId },
        { nameof(LocalAdvertisementResultDTO.PlaceIdSearch), SavedLocalPlacesQueryColumnNameConstants.PlaceIdSearch },
        { nameof(LocalAdvertisementResultDTO.Lsig), SavedLocalPlacesQueryColumnNameConstants.Lsig },
        { nameof(LocalAdvertisementResultDTO.Thumbnail), SavedLocalPlacesQueryColumnNameConstants.Thumbnail },
        { nameof(LocalAdvertisementResultDTO.GpsCoordinates), SavedLocalPlacesQueryColumnNameConstants.GpsCoordinates },
        { nameof(LocalAdvertisementResultDTO.ServiceOptions), SavedLocalPlacesQueryColumnNameConstants.ServiceOptions },
        { nameof(LocalAdvertisementResultDTO.Price), SavedLocalPlacesQueryColumnNameConstants.Price }
    };

    private static readonly Dictionary<string, string> _localMapMappings = new()
    {
        { nameof(LocalMapDTO.Image), SavedLocalPlacesQueryColumnNameConstants.Image }
    };

    private static readonly Dictionary<string, string> _gpsCoordinatesMappings = new()
    {
        { nameof(GpsCoordinatesDTO.Latitude), SavedLocalPlacesQueryColumnNameConstants.Latitude },
        { nameof(GpsCoordinatesDTO.Longitude), SavedLocalPlacesQueryColumnNameConstants.Longitude }
    };

    private static readonly Dictionary<string, string> _serviceOptionsMappings = new()
    {
        { nameof(ServiceOptionsDTO.DineIn), SavedLocalPlacesQueryColumnNameConstants.DineIn },
        { nameof(ServiceOptionsDTO.Takeout), SavedLocalPlacesQueryColumnNameConstants.Takeout },
        { nameof(ServiceOptionsDTO.Delivery), SavedLocalPlacesQueryColumnNameConstants.Delivery },
        { nameof(ServiceOptionsDTO.NoDelivery), SavedLocalPlacesQueryColumnNameConstants.NoDelivery },
        { nameof(ServiceOptionsDTO.InStorePickup), SavedLocalPlacesQueryColumnNameConstants.InStorePickup },
        { nameof(ServiceOptionsDTO.InStoreShopping), SavedLocalPlacesQueryColumnNameConstants.InStoreShopping },
        { nameof(ServiceOptionsDTO.CurbsidePickup), SavedLocalPlacesQueryColumnNameConstants.CurbsidePickup },
        { nameof(ServiceOptionsDTO.NoContactDelivery), SavedLocalPlacesQueryColumnNameConstants.NoContactDelivery },
        { nameof(ServiceOptionsDTO.Reservable), SavedLocalPlacesQueryColumnNameConstants.Reservable },
        { nameof(ServiceOptionsDTO.WheelchairAccessible), SavedLocalPlacesQueryColumnNameConstants.WheelchairAccessible }
    };

    private static readonly Dictionary<string, string> _placeLinksMappings = new()
    {
        { nameof(PlaceLinksDTO.Phone), SavedLocalPlacesQueryColumnNameConstants.PhoneLink },
        { nameof(PlaceLinksDTO.Directions), SavedLocalPlacesQueryColumnNameConstants.Directions },
        { nameof(PlaceLinksDTO.Website), SavedLocalPlacesQueryColumnNameConstants.Website },
        { nameof(PlaceLinksDTO.Order), SavedLocalPlacesQueryColumnNameConstants.Order }
    };

    private static readonly Dictionary<string, string> _discoverMorePlacesMappings = new()
    {
        { nameof(DiscoverMorePlaceDTO.Title), SavedLocalPlacesQueryColumnNameConstants.Title },
        { nameof(DiscoverMorePlaceDTO.Link), SavedLocalPlacesQueryColumnNameConstants.Link },
        { nameof(DiscoverMorePlaceDTO.SerpApiLink), SavedLocalPlacesQueryColumnNameConstants.SerpApiLink },
        { nameof(DiscoverMorePlaceDTO.Thumbnail), SavedLocalPlacesQueryColumnNameConstants.Thumbnail },
        { nameof(DiscoverMorePlaceDTO.Places), SavedLocalPlacesQueryColumnNameConstants.Places },
        { nameof(DiscoverMorePlaceDTO.Images), SavedLocalPlacesQueryColumnNameConstants.Images }
    };

    protected override GetLocalPlacesResponseDTO FilterResponse(GetLocalPlacesResponseDTO response, List<string> activeColumns)
    {
        var filteredResponse = new GetLocalPlacesResponseDTO();

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.SearchMetadata, activeColumns))
            filteredResponse.SearchMetadata = response.SearchMetadata;

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.SearchParameters, activeColumns))
            filteredResponse.SearchParameters = response.SearchParameters;

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.AdResults, activeColumns))
            filteredResponse.AdResults = response.AdResults?.Select(ad => CreateFilteredInstance(ad, activeColumns, _adResultMappings)).ToList();

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.LocalMap, activeColumns))
            filteredResponse.LocalMap = response.LocalMap != null ? CreateFilteredInstance(response.LocalMap, activeColumns, _localMapMappings) : null;

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.LocalResults, activeColumns))
            filteredResponse.LocalResults = response.LocalResults.Select(result => CreateFilteredInstance(result, activeColumns, _localResultMappings)).ToList();

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces, activeColumns))
            filteredResponse.DiscoverMorePlaces = response.DiscoverMorePlaces?.Select(place => CreateFilteredInstance(place, activeColumns, _discoverMorePlacesMappings)).ToList();

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.Pagination, activeColumns))
            filteredResponse.Pagination = response.Pagination;

        if (ShouldIncludeContainer(SavedLocalPlacesQueryColumnNameConstants.SerpApiPagination, activeColumns))
            filteredResponse.SerpApiPagination = response.SerpApiPagination;

        FilterNestedObjects(filteredResponse, activeColumns);

        return filteredResponse;
    }

    private static bool ShouldIncludeContainer(string containerName, List<string> activeColumns)
    {
        if (IsColumnActive(containerName, activeColumns))
            return true;

        return containerName switch
        {
            SavedLocalPlacesQueryColumnNameConstants.LocalResults => activeColumns.Any(IsLocalResultColumn),
            SavedLocalPlacesQueryColumnNameConstants.AdResults => activeColumns.Any(IsAdResultColumn),
            SavedLocalPlacesQueryColumnNameConstants.SearchMetadata => activeColumns.Any(IsSearchMetadataColumn),
            SavedLocalPlacesQueryColumnNameConstants.SearchParameters => activeColumns.Any(IsSearchParametersColumn),
            SavedLocalPlacesQueryColumnNameConstants.LocalMap => activeColumns.Any(IsLocalMapColumn),
            SavedLocalPlacesQueryColumnNameConstants.DiscoverMorePlaces =>
                activeColumns.Any(IsDiscoverMorePlacesColumn),
            _ => false
        };
    }

    private static bool IsLocalResultColumn(string columnName) => columnName is SavedLocalPlacesQueryColumnNameConstants.Position or SavedLocalPlacesQueryColumnNameConstants.Title or SavedLocalPlacesQueryColumnNameConstants.Rating or SavedLocalPlacesQueryColumnNameConstants.ReviewsOriginal or SavedLocalPlacesQueryColumnNameConstants.Reviews or SavedLocalPlacesQueryColumnNameConstants.Price or SavedLocalPlacesQueryColumnNameConstants.Type or SavedLocalPlacesQueryColumnNameConstants.Address or SavedLocalPlacesQueryColumnNameConstants.Description or SavedLocalPlacesQueryColumnNameConstants.PlaceId or SavedLocalPlacesQueryColumnNameConstants.PlaceIdSearch or SavedLocalPlacesQueryColumnNameConstants.ProviderId or SavedLocalPlacesQueryColumnNameConstants.Lsig or SavedLocalPlacesQueryColumnNameConstants.Thumbnail or SavedLocalPlacesQueryColumnNameConstants.Images or SavedLocalPlacesQueryColumnNameConstants.GpsCoordinates or SavedLocalPlacesQueryColumnNameConstants.ServiceOptions or SavedLocalPlacesQueryColumnNameConstants.Phone or SavedLocalPlacesQueryColumnNameConstants.Hours or SavedLocalPlacesQueryColumnNameConstants.Extensions or SavedLocalPlacesQueryColumnNameConstants.Links or SavedLocalPlacesQueryColumnNameConstants.Latitude or SavedLocalPlacesQueryColumnNameConstants.Longitude or SavedLocalPlacesQueryColumnNameConstants.DineIn or SavedLocalPlacesQueryColumnNameConstants.Takeout or SavedLocalPlacesQueryColumnNameConstants.Delivery or SavedLocalPlacesQueryColumnNameConstants.NoDelivery or SavedLocalPlacesQueryColumnNameConstants.InStorePickup or SavedLocalPlacesQueryColumnNameConstants.InStoreShopping or SavedLocalPlacesQueryColumnNameConstants.CurbsidePickup or SavedLocalPlacesQueryColumnNameConstants.NoContactDelivery or SavedLocalPlacesQueryColumnNameConstants.Reservable or SavedLocalPlacesQueryColumnNameConstants.WheelchairAccessible or SavedLocalPlacesQueryColumnNameConstants.PhoneLink or SavedLocalPlacesQueryColumnNameConstants.Directions or SavedLocalPlacesQueryColumnNameConstants.Website or SavedLocalPlacesQueryColumnNameConstants.Order;

    private static bool IsAdResultColumn(string columnName) => columnName == SavedLocalPlacesQueryColumnNameConstants.AdTitle || columnName == SavedLocalPlacesQueryColumnNameConstants.DisplayedLink || IsLocalResultColumn(columnName);

    private static bool IsSearchMetadataColumn(string columnName) => columnName is SavedLocalPlacesQueryColumnNameConstants.Id or SavedLocalPlacesQueryColumnNameConstants.Status or SavedLocalPlacesQueryColumnNameConstants.JsonEndpoint or SavedLocalPlacesQueryColumnNameConstants.CreatedAt or SavedLocalPlacesQueryColumnNameConstants.ProcessedAt or SavedLocalPlacesQueryColumnNameConstants.RawHtmlFile or SavedLocalPlacesQueryColumnNameConstants.TotalTimeTaken;

    private static bool IsSearchParametersColumn(string columnName) => columnName is SavedLocalPlacesQueryColumnNameConstants.Engine or SavedLocalPlacesQueryColumnNameConstants.Query or SavedLocalPlacesQueryColumnNameConstants.LocationRequested or SavedLocalPlacesQueryColumnNameConstants.LocationUsed or SavedLocalPlacesQueryColumnNameConstants.GoogleDomain or SavedLocalPlacesQueryColumnNameConstants.LanguageCode or SavedLocalPlacesQueryColumnNameConstants.CountryCode or SavedLocalPlacesQueryColumnNameConstants.Device or SavedLocalPlacesQueryColumnNameConstants.Uule;

    private static bool IsLocalMapColumn(string columnName) => columnName == SavedLocalPlacesQueryColumnNameConstants.Image;

    private static bool IsDiscoverMorePlacesColumn(string columnName) => columnName is SavedLocalPlacesQueryColumnNameConstants.Places or SavedLocalPlacesQueryColumnNameConstants.Link or SavedLocalPlacesQueryColumnNameConstants.SerpApiLink;

    private static void FilterNestedObjects(GetLocalPlacesResponseDTO response, List<string> activeColumns)
    {
        foreach (var result in response.LocalResults)
        {
            if (result.GpsCoordinates != null) result.GpsCoordinates = CreateFilteredInstance(result.GpsCoordinates, activeColumns, _gpsCoordinatesMappings);

            if (result.ServiceOptions != null) result.ServiceOptions = CreateFilteredInstance(result.ServiceOptions, activeColumns, _serviceOptionsMappings);

            if (result.Links != null) result.Links = CreateFilteredInstance(result.Links, activeColumns, _placeLinksMappings);
        }

        if (response.AdResults == null)
            return;

        foreach (var ad in response.AdResults)
        {
            if (ad.GpsCoordinates != null) ad.GpsCoordinates = CreateFilteredInstance(ad.GpsCoordinates, activeColumns, _gpsCoordinatesMappings);

            if (ad.ServiceOptions != null) ad.ServiceOptions = CreateFilteredInstance(ad.ServiceOptions, activeColumns, _serviceOptionsMappings);
        }
    }
}
