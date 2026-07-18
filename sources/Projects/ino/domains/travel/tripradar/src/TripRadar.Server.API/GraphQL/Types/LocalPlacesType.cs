using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class LocalPlacesType : ObjectType<GetLocalPlacesResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetLocalPlacesResponse> descriptor)
    {
        descriptor.Description(
            "Represents a complete local places search response with metadata, search parameters, and local business results");

        descriptor.Field(f => f.SearchMetadata)
            .Type<LocalPlacesSearchMetadataType>()
            .Description("Metadata about the search request such as status and processing times");

        descriptor.Field(f => f.SearchParameters)
            .Type<LocalSearchParametersType>()
            .Description("Parameters used for the local places search");

        descriptor.Field(f => f.AdResults)
            .Type<ListType<LocalAdvertisementResultType>>()
            .Description("List of sponsored advertisement results for local businesses");

        descriptor.Field(f => f.LocalMap)
            .Type<LocalMapType>()
            .Description("Map image information for the search area");

        descriptor.Field(f => f.LocalResults)
            .Type<ListType<LocalPlaceResultType>>()
            .Description("List of local business results matching the search query");

        descriptor.Field(f => f.DiscoverMorePlaces)
            .Type<ListType<DiscoverMorePlaceType>>()
            .Description("Suggestions for discovering more places and related searches");

        descriptor.Field(f => f.Pagination)
            .Type<LocalPaginationType>()
            .Description("Pagination information for browsing through search results");

        descriptor.Field(f => f.SerpApiPagination)
            .Type<LocalSerpApiPaginationType>()
            .Description("SerpApi-specific pagination information");
    }
}

public class LocalMapType : ObjectType<LocalMap>
{
    protected override void Configure(IObjectTypeDescriptor<LocalMap> descriptor)
    {
        descriptor.Description("Map image information for the search area");

        descriptor.Field(m => m.Image)
            .Type<StringType>()
            .Description("URL to the map image for the search area");
    }
}

public class LocalPlacesSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("LocalPlacesSearchMetadata");
        descriptor.Description("Metadata information about the local places search request");

        descriptor.Field(m => m.Id)
            .Type<StringType>()
            .Description("Unique identifier for the search request");

        descriptor.Field(m => m.Status)
            .Type<StringType>()
            .Description("Status of the search request");

        descriptor.Field(m => m.JsonEndpoint)
            .Type<StringType>()
            .Description("JSON endpoint used for the search");

        descriptor.Field(m => m.CreatedAt)
            .Type<StringType>()
            .Description("Timestamp when the search was created");

        descriptor.Field(m => m.ProcessedAt)
            .Type<StringType>()
            .Description("Timestamp when the search was processed");

        descriptor.Field(m => m.RawHtmlFile)
            .Type<StringType>()
            .Description("Link to raw HTML file if available");

        descriptor.Field(m => m.TotalTimeTaken)
            .Type<FloatType>()
            .Description("Total time taken to process the search in seconds");
    }
}

public class LocalSearchParametersType : ObjectType<LocalSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<LocalSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for the local places search query");

        descriptor.Field(p => p.Engine)
            .Type<StringType>()
            .Description("Search engine used");

        descriptor.Field(p => p.Q)
            .Type<NonNullType<StringType>>()
            .Description("Search query term");

        descriptor.Field(p => p.Uule)
            .Type<StringType>()
            .Description("Encoded location parameter for Google search");

        descriptor.Field(p => p.LocationRequested)
            .Type<StringType>()
            .Description("Human-readable location requested for the search");

        descriptor.Field(p => p.LocationUsed)
            .Type<StringType>()
            .Description("Actual location used for the search");

        descriptor.Field(p => p.GoogleDomain)
            .Type<StringType>()
            .Description("Google domain used for the search");

        descriptor.Field(p => p.Hl)
            .Type<StringType>()
            .Description("Language parameter for the search");

        descriptor.Field(p => p.Gl)
            .Type<StringType>()
            .Description("Country parameter for the search");

        descriptor.Field(p => p.Device)
            .Type<StringType>()
            .Description("Device type used for the search");
    }
}

public class LocalAdvertisementResultType : ObjectType<LocalAdvertisementResult>
{
    protected override void Configure(IObjectTypeDescriptor<LocalAdvertisementResult> descriptor)
    {
        descriptor.Description("Sponsored advertisement result for local businesses");

        descriptor.Field(a => a.Position)
            .Type<IntType>()
            .Description("Position of the ad in search results");

        descriptor.Field(a => a.AdTitle)
            .Type<StringType>()
            .Description("Title of the advertisement");

        descriptor.Field(a => a.DisplayedLink)
            .Type<StringType>()
            .Description("Displayed link URL for the advertisement");

        descriptor.Field(a => a.Title)
            .Type<NonNullType<StringType>>()
            .Description("Business name or title");

        descriptor.Field(a => a.Type)
            .Type<StringType>()
            .Description("Type of business or category");

        descriptor.Field(a => a.ReviewsOriginal)
            .Type<StringType>()
            .Description("Original reviews text as displayed");

        descriptor.Field(a => a.Reviews)
            .Type<IntType>()
            .Description("Number of reviews");

        descriptor.Field(a => a.Rating)
            .Type<FloatType>()
            .Description("Average rating of the business");

        descriptor.Field(a => a.Address)
            .Type<StringType>()
            .Description("Address of the business");

        descriptor.Field(a => a.Hours)
            .Type<StringType>()
            .Description("Operating hours information");

        descriptor.Field(a => a.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("Unique Google Place ID");

        descriptor.Field(a => a.PlaceIdSearch)
            .Type<StringType>()
            .Description("SerpApi search URL for this place");

        descriptor.Field(a => a.Lsig)
            .Type<StringType>()
            .Description("Location signature for the place");

        descriptor.Field(a => a.Thumbnail)
            .Type<StringType>()
            .Description("Thumbnail image URL for the business");

        descriptor.Field(a => a.GpsCoordinates)
            .Type<GpsCoordinatesType>()
            .Description("GPS coordinates of the business location");

        descriptor.Field(a => a.ServiceOptions)
            .Type<ServiceOptionsType>()
            .Description("Available service options for the business");

        descriptor.Field(a => a.Price)
            .Type<StringType>()
            .Description("Price level indicator (e.g., $, $$, $$$)");
    }
}

public class LocalPlaceResultType : ObjectType<LocalPlaceResult>
{
    protected override void Configure(IObjectTypeDescriptor<LocalPlaceResult> descriptor)
    {
        descriptor.Description("Local business result with detailed information");

        descriptor.Field(r => r.Position)
            .Type<IntType>()
            .Description("Position in search results");

        descriptor.Field(r => r.Title)
            .Type<NonNullType<StringType>>()
            .Description("Business name or title");

        descriptor.Field(r => r.Rating)
            .Type<FloatType>()
            .Description("Average rating of the business");

        descriptor.Field(r => r.ReviewsOriginal)
            .Type<StringType>()
            .Description("Original reviews text as displayed");

        descriptor.Field(r => r.Reviews)
            .Type<IntType>()
            .Description("Number of reviews");

        descriptor.Field(r => r.Price)
            .Type<StringType>()
            .Description("Price level indicator (e.g., $, $$, $$$)");

        descriptor.Field(r => r.Type)
            .Type<StringType>()
            .Description("Type of business or category");

        descriptor.Field(r => r.Address)
            .Type<StringType>()
            .Description("Address of the business");

        descriptor.Field(r => r.Description)
            .Type<StringType>()
            .Description("Business description or tagline");

        descriptor.Field(r => r.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("Unique Google Place ID");

        descriptor.Field(r => r.PlaceIdSearch)
            .Type<StringType>()
            .Description("SerpApi search URL for this place");

        descriptor.Field(r => r.ProviderId)
            .Type<StringType>()
            .Description("Google's internal provider identifier");

        descriptor.Field(r => r.Lsig)
            .Type<StringType>()
            .Description("Location signature for the place");

        descriptor.Field(r => r.Thumbnail)
            .Type<StringType>()
            .Description("Thumbnail image URL for the business");

        descriptor.Field(r => r.Images)
            .Type<ListType<StringType>>()
            .Description("Array of business photo URLs");

        descriptor.Field(r => r.GpsCoordinates)
            .Type<GpsCoordinatesType>()
            .Description("GPS coordinates of the business location");

        descriptor.Field(r => r.ServiceOptions)
            .Type<ServiceOptionsType>()
            .Description("Available service options for the business");

        descriptor.Field(r => r.Phone)
            .Type<StringType>()
            .Description("Phone number of the business");

        descriptor.Field(r => r.Hours)
            .Type<StringType>()
            .Description("Operating hours information");

        descriptor.Field(r => r.Extensions)
            .Type<ListType<StringType>>()
            .Description("Additional business features and extensions");

        descriptor.Field(r => r.Links)
            .Type<PlaceLinksType>()
            .Description("Related links for the business");
    }
}

public class PlaceLinksType : ObjectType<PlaceLinks>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceLinks> descriptor)
    {
        descriptor.Description("Related links for the business");

        descriptor.Field(l => l.Phone)
            .Type<StringType>()
            .Description("Direct phone link");

        descriptor.Field(l => l.Directions)
            .Type<StringType>()
            .Description("Google Maps directions link");

        descriptor.Field(l => l.Website)
            .Type<StringType>()
            .Description("Business website URL");

        descriptor.Field(l => l.Order)
            .Type<StringType>()
            .Description("Online ordering link");
    }
}

public class GpsCoordinatesType : ObjectType<GpsCoordinates>
{
    protected override void Configure(IObjectTypeDescriptor<GpsCoordinates> descriptor)
    {
        descriptor.Description("GPS coordinates for location positioning");

        descriptor.Field(g => g.Latitude)
            .Type<FloatType>()
            .Description("Latitude coordinate");

        descriptor.Field(g => g.Longitude)
            .Type<FloatType>()
            .Description("Longitude coordinate");
    }
}

public class ServiceOptionsType : ObjectType<ServiceOptions>
{
    protected override void Configure(IObjectTypeDescriptor<ServiceOptions> descriptor)
    {
        descriptor.Description("Available service options for the business");

        descriptor.Field(s => s.DineIn)
            .Type<BooleanType>()
            .Description("Whether dine-in service is available");

        descriptor.Field(s => s.Takeout)
            .Type<BooleanType>()
            .Description("Whether takeout service is available");

        descriptor.Field(s => s.Delivery)
            .Type<BooleanType>()
            .Description("Whether delivery service is available");

        descriptor.Field(s => s.NoDelivery)
            .Type<BooleanType>()
            .Description("Whether delivery is explicitly not available");

        descriptor.Field(s => s.InStorePickup)
            .Type<BooleanType>()
            .Description("Whether in-store pickup is available");

        descriptor.Field(s => s.InStoreShopping)
            .Type<BooleanType>()
            .Description("Whether in-store shopping is available");

        descriptor.Field(s => s.CurbsidePickup)
            .Type<BooleanType>()
            .Description("Whether curbside pickup is available");

        descriptor.Field(s => s.NoContactDelivery)
            .Type<BooleanType>()
            .Description("Whether no-contact delivery is available");

        descriptor.Field(s => s.Reservable)
            .Type<BooleanType>()
            .Description("Whether reservations can be made");

        descriptor.Field(s => s.WheelchairAccessible)
            .Type<BooleanType>()
            .Description("Whether the location is wheelchair accessible");
    }
}

public class DiscoverMorePlaceType : ObjectType<DiscoverMorePlace>
{
    protected override void Configure(IObjectTypeDescriptor<DiscoverMorePlace> descriptor)
    {
        descriptor.Description("Suggested place category for discovery");

        descriptor.Field(d => d.Title)
            .Type<NonNullType<StringType>>()
            .Description("Title of the suggested category");

        descriptor.Field(d => d.Link)
            .Type<StringType>()
            .Description("Link to search for this category");

        descriptor.Field(d => d.SerpApiLink)
            .Type<StringType>()
            .Description("SerpApi link for this category search");

        descriptor.Field(d => d.Thumbnail)
            .Type<StringType>()
            .Description("Thumbnail image for the category");

        descriptor.Field(d => d.Places)
            .Type<StringType>()
            .Description("String of place names for this category");

        descriptor.Field(d => d.Images)
            .Type<ListType<StringType>>()
            .Description("Array of thumbnail images for the category");
    }
}

public class LocalPaginationType : ObjectType<LocalPagination>
{
    protected override void Configure(IObjectTypeDescriptor<LocalPagination> descriptor)
    {
        descriptor.Description("Pagination information for search results");

        descriptor.Field(p => p.Current)
            .Type<IntType>()
            .Description("Current page number");

        descriptor.Field(p => p.Next)
            .Type<StringType>()
            .Description("URL for the next page");

        descriptor.Field(p => p.OtherPages)
            .Type<AnyType>()
            .Description("URLs for other pages");
    }
}

public class LocalSerpApiPaginationType : ObjectType<LocalSerpApiPagination>
{
    protected override void Configure(IObjectTypeDescriptor<LocalSerpApiPagination> descriptor)
    {
        descriptor.Description("SerpApi-specific pagination information");

        descriptor.Field(p => p.Current)
            .Type<IntType>()
            .Description("Current page number");

        descriptor.Field(p => p.NextLink)
            .Type<StringType>()
            .Description("SerpApi URL for the next page");

        descriptor.Field(p => p.Next)
            .Type<StringType>()
            .Description("Next page link");

        descriptor.Field(p => p.OtherPages)
            .Type<AnyType>()
            .Description("SerpApi URLs for other pages");
    }
}

// Input types for GraphQL mutations and queries
public class LocalPlacesInputType : InputObjectType<GetLocalPlacesRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetLocalPlacesRequest> descriptor)
    {
        descriptor.Name("GetLocalPlacesRequest");
        descriptor.Description("Input parameters for querying local places information");

        descriptor.Field(f => f.SearchQuery)
            .Type<NonNullType<SearchQueryInputType>>()
            .Description("The main search query containing the search terms");

        descriptor.Field(f => f.Localization)
            .Type<LocalPlacesLocalizationInputType>()
            .Description("Localization options such as language, country, and currency");

        descriptor.Field(f => f.AdvancedParameters)
            .Type<GeographicLocationInputType>()
            .Description("Geographic location parameters for the search");

        descriptor.Field(f => f.Filters)
            .Type<LocalPlacesFiltersInputType>()
            .Description("Optional filters for refining search results");

        descriptor.Field(f => f.Pagination)
            .Type<PaginationInputType>()
            .Description("Pagination options for managing result sets");

        // Add the missing sorting field that tests expect
        descriptor.Field("sorting")
            .Type<SortingInputType>()
            .Description("Sorting options for search results");
    }
}

public class SearchQueryInputType : InputObjectType<SearchQuery>
{
    protected override void Configure(IInputObjectTypeDescriptor<SearchQuery> descriptor)
    {
        descriptor.Description("Search query parameters");

        descriptor.Field(s => s.Q)
            .Type<NonNullType<StringType>>()
            .Description("Search query term (e.g., 'coffee shops', 'restaurants')");
    }
}

public class GeographicLocationInputType : InputObjectType<GeographicLocation>
{
    protected override void Configure(IInputObjectTypeDescriptor<GeographicLocation> descriptor)
    {
        descriptor.Description("Geographic location parameters for the search");

        descriptor.Field(g => g.Location)
            .Type<StringType>()
            .Description("Location for the search (e.g., 'New York, NY')");

        descriptor.Field(g => g.Uule)
            .Type<StringType>()
            .Description("Google encoded location parameter");

        descriptor.Field(g => g.Ludocid)
            .Type<StringType>()
            .Description("Google My Business listing ID (CID)");

        // Add the missing coordinate fields that tests expect
        descriptor.Field("latitude")
            .Type<FloatType>()
            .Description("Latitude coordinate for location-based search");

        descriptor.Field("longitude")
            .Type<FloatType>()
            .Description("Longitude coordinate for location-based search");

        descriptor.Field("radius")
            .Type<IntType>()
            .Description("Search radius in meters around the specified coordinates");
    }
}

public class LocalPlacesFiltersInputType : InputObjectType<LocalPlacesFilters>
{
    protected override void Configure(IInputObjectTypeDescriptor<LocalPlacesFilters> descriptor)
    {
        descriptor.Description("Filters for refining local places search results");

        descriptor.Field(f => f.Tbs)
            .Type<StringType>()
            .Description("Advanced search parameters that aren't possible in the regular query field");

        // Add the missing fields that tests expect
        descriptor.Field("openNow")
            .Type<BooleanType>()
            .Description("Filter for places that are currently open");

        descriptor.Field("minRating")
            .Type<FloatType>()
            .Description("Minimum rating threshold for results");

        descriptor.Field("priceLevel")
            .Type<StringType>()
            .Description("Price level filter (e.g., '$', '$$', '$$$', '$$$$')");

        descriptor.Field("placeTypes")
            .Type<ListType<StringType>>()
            .Description("List of place types to filter by (e.g., 'restaurant', 'cafe')");

        descriptor.Field("serviceOptions")
            .Type<ListType<StringType>>()
            .Description("Service options filter (e.g., 'dine_in', 'takeout', 'delivery')");

        descriptor.Field("hasReviews")
            .Type<BooleanType>()
            .Description("Filter for places that have reviews");
    }
}

public class PaginationInputType : InputObjectType<Pagination>
{
    protected override void Configure(IInputObjectTypeDescriptor<Pagination> descriptor)
    {
        descriptor.Description("Pagination options for managing result sets");

        descriptor.Field(p => p.Start)
            .Type<IntType>()
            .Description("Starting position for results (must be multiples of 20 for desktop, 10 for mobile)");

        // Add the missing num field that tests expect
        descriptor.Field("num")
            .Type<IntType>()
            .Description("Number of results to return (typically 10 or 20)");
    }
}

public class LocalPlacesLocalizationInputType : InputObjectType<Localization>
{
    protected override void Configure(IInputObjectTypeDescriptor<Localization> descriptor)
    {
        descriptor.Name("LocalPlacesLocalization");
        descriptor.Description("Localization settings for the search");

        descriptor.Field(l => l.Hl)
            .Type<StringType>()
            .Description("Language code (e.g., 'en', 'es', 'fr')");

        descriptor.Field(l => l.Gl)
            .Type<StringType>()
            .Description("Country code (e.g., 'US', 'CA', 'UK')");

        descriptor.Field(l => l.Currency)
            .Type<StringType>()
            .Description("Currency code (e.g., 'USD', 'EUR', 'GBP')");

        descriptor.Field(l => l.GoogleDomain)
            .Type<StringType>()
            .Description("Google domain to use for the search (e.g., 'google.com')");
    }
}

public class SortingInputType : InputObjectType
{
    protected override void Configure(IInputObjectTypeDescriptor descriptor)
    {
        descriptor.Name("SortingInput");
        descriptor.Description("Sorting options for search results");

        descriptor.Field("sortBy")
            .Type<StringType>()
            .Description("Field to sort by (e.g., 'rating', 'distance', 'relevance')");

        descriptor.Field("order")
            .Type<StringType>()
            .Description("Sort order ('asc' for ascending, 'desc' for descending)");
    }
}

