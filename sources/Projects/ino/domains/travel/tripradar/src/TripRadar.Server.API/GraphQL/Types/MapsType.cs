using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class MapsType : ObjectType<GetMapsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetMapsResponse> descriptor)
    {
        descriptor.Name("Maps");
        descriptor.Description("Detailed place information from Google Maps API via SerpApi. " +
                               "Provides comprehensive data about a specific place including reviews, photos, hours, and more.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<MapsSearchMetadataType>()
            .Description("Metadata about the search request and response");

        descriptor.Field(f => f.SearchParameters)
            .Type<MapsSearchParametersType>()
            .Description("Parameters used in the search request");

        descriptor.Field(f => f.LocalResults)
            .Type<ListType<MapsPlaceResultType>>()
            .Description("List of local search results (used when type=search)");

        descriptor.Field(f => f.PlaceResults)
            .Type<MapsPlaceResultType>()
            .Description("Detailed information about a specific place (used when querying by place_id)");
    }
}

public class MapsSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("MapsSearchMetadata");
        descriptor.Description("Metadata information for Maps API search requests");

        descriptor.Field(f => f.Id)
            .Description("Unique identifier for the search request");

        descriptor.Field(f => f.Status)
            .Description("Status of the search request");

        descriptor.Field(f => f.JsonEndpoint)
            .Description("JSON endpoint URL");

        descriptor.Field(f => f.CreatedAt)
            .Description("When the search was created");

        descriptor.Field(f => f.ProcessedAt)
            .Description("When the search was processed");

        descriptor.Field(f => f.RawHtmlFile)
            .Description("Raw HTML file path");

        descriptor.Field(f => f.PrettifyHtmlFile)
            .Description("Prettified HTML file path");

        descriptor.Field(f => f.GoogleMapsUrl)
            .Description("Google Maps URL for the request");

        descriptor.Field(f => f.GoogleMapsDirectionsUrl)
            .Description("Google Maps directions URL for the request");

        descriptor.Field(f => f.TotalTimeTaken)
            .Description("Total time taken for the request");
    }
}

public class MapsSearchParametersType : ObjectType<MapsSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<MapsSearchParameters> descriptor)
    {
        descriptor.Name("MapsSearchParameters");
        descriptor.Description("Search parameters used for the Maps API request");

        descriptor.Field(f => f.Engine)
            .Description("API engine used (google_maps)");

        descriptor.Field(f => f.Type)
            .Description("Search type (place or search)");

        descriptor.Field(f => f.PlaceId)
            .Description("Google Place ID used for the search");

        descriptor.Field(f => f.Data)
            .Description("Alternative data parameter used");

        descriptor.Field(f => f.GoogleDomain)
            .Description("Google domain used for the search");

        descriptor.Field(f => f.Hl)
            .Description("Language code used");

        descriptor.Field(f => f.Gl)
            .Description("Country code used");
    }
}

public class MapsPlaceResultType : ObjectType<MapsPlaceResult>
{
    protected override void Configure(IObjectTypeDescriptor<MapsPlaceResult> descriptor)
    {
        descriptor.Name("MapsPlaceResult");
        descriptor.Description("Comprehensive place information from Google Maps");

        descriptor.Field(f => f.Title)
            .Type<NonNullType<StringType>>()
            .Description("Place name or title");

        descriptor.Field(f => f.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("Unique Google Place ID");

        descriptor.Field(f => f.DataId)
            .Description("Alternative data identifier");

        descriptor.Field(f => f.DataCid)
            .Description("Alternative CID identifier");

        descriptor.Field(f => f.ReviewsLink)
            .Description("Link to reviews for this place");

        descriptor.Field(f => f.PhotosLink)
            .Description("Link to photos for this place");

        descriptor.Field(f => f.GpsCoordinates)
            .Type<GpsCoordinatesType>()
            .Description("GPS coordinates of the place");

        descriptor.Field(f => f.PlaceIdSearch)
            .Description("SerpApi search URL for this place");

        descriptor.Field(f => f.ProviderId)
            .Description("Provider identifier");

        descriptor.Field(f => f.Thumbnail)
            .Description("Thumbnail image URL");

        descriptor.Field(f => f.SerpapiThumbnail)
            .Description("SerpApi thumbnail URL");

        descriptor.Field(f => f.Rating)
            .Type<FloatType>()
            .Description("Average rating");

        descriptor.Field(f => f.Reviews)
            .Type<IntType>()
            .Description("Number of reviews");

        descriptor.Field(f => f.Price)
            .Description("Price level indicator");

        descriptor.Field(f => f.Type)
            .Description("Place type (can be single type or comma-separated types)");

        descriptor.Field(f => f.TypeIds)
            .Type<ListType<StringType>>()
            .Description("List of place type IDs");

        descriptor.Field(f => f.Address)
            .Description("Place address");

        descriptor.Field(f => f.Website)
            .Description("Place website URL");

        descriptor.Field(f => f.Phone)
            .Description("Place phone number");

        descriptor.Field(f => f.OpenState)
            .Description("Current open/closed state");

        descriptor.Field(f => f.Hours)
            .Type<AnyType>()
            .Description("Operating hours as a human-readable string");

        descriptor.Field(f => f.Description)
            .Description("Place description");

        descriptor.Field(f => f.ServiceOptions)
            .Type<ServiceOptionsType>()
            .Description("Available service options");

        descriptor.Field(f => f.Extensions)
            .Type<ListType<MapsExtensionType>>()
            .Description("Additional place information");
    }
}

public class MapsExtensionType : ObjectType<MapsExtension>
{
    protected override void Configure(IObjectTypeDescriptor<MapsExtension> descriptor)
    {
        descriptor.Name("MapsExtension");
        descriptor.Description("Additional place information and attributes");

        descriptor.Field(f => f.Highlights)
            .Type<ListType<StringType>>()
            .Description("Place highlights");

        descriptor.Field(f => f.PopularFor)
            .Type<ListType<StringType>>()
            .Description("What the place is popular for");

        descriptor.Field(f => f.Accessibility)
            .Type<ListType<StringType>>()
            .Description("Accessibility features");

        descriptor.Field(f => f.Crowd)
            .Type<ListType<StringType>>()
            .Description("Crowd information");

        descriptor.Field(f => f.Payments)
            .Type<ListType<StringType>>()
            .Description("Accepted payment methods");

        descriptor.Field(f => f.Planning)
            .Type<ListType<StringType>>()
            .Description("Planning information");
    }
}

public class MapsInputType : InputObjectType<GetMapsRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetMapsRequest> descriptor)
    {
        descriptor.Name("GetMapsRequest");
        descriptor.Description("Input parameters for querying Maps information");

        descriptor.Field(f => f.PlaceId)
            .Type<StringType>()
            .Description("Google Place ID for specific place lookup");

        descriptor.Field(f => f.Data)
            .Type<StringType>()
            .Description("Data parameter for place lookup");

        descriptor.Field(f => f.SearchQuery)
            .Type<MapsSearchQueryInputType>()
            .Description("Search query for finding places");

        descriptor.Field(f => f.Ll)
            .Type<StringType>()
            .Description("Geographic location in format @latitude,longitude,zoom");

        descriptor.Field(f => f.Type)
            .Type<StringType>()
            .Description("Search type: 'search' or 'place'");

        descriptor.Field(f => f.Localization)
            .Type<MapsLocalizationInputType>()
            .Description("Localization options such as language, country, and currency");

        descriptor.Field(f => f.Pagination)
            .Type<MapsPaginationInputType>()
            .Description("Pagination options for managing result sets");

        descriptor.Field(f => f.NoCache)
            .Type<BooleanType>()
            .Description("Whether to bypass cache");
    }
}

public class MapsSearchQueryInputType : InputObjectType<SearchQuery>
{
    protected override void Configure(IInputObjectTypeDescriptor<SearchQuery> descriptor)
    {
        descriptor.Name("MapsSearchQuery");
        descriptor.Description("Search query for Maps search requests");

        descriptor.Field(f => f.Q)
            .Type<NonNullType<StringType>>()
            .Description("Search query string");
    }
}

public class MapsLocalizationInputType : InputObjectType<Localization>
{
    protected override void Configure(IInputObjectTypeDescriptor<Localization> descriptor)
    {
        descriptor.Name("MapsLocalization");
        descriptor.Description("Localization settings for the Maps search");

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
            .Description("Google domain for localization (e.g., 'google.com', 'google.fr')");
    }
}

public class MapsPaginationInputType : InputObjectType<MapsPagination>
{
    protected override void Configure(IInputObjectTypeDescriptor<MapsPagination> descriptor)
    {
        descriptor.Name("MapsPagination");
        descriptor.Description("Pagination options for Maps search results");

        descriptor.Field(p => p.Start)
            .Type<IntType>()
            .Description("Starting position for results (0-100)");
    }
}

public class MapsMenuType : ObjectType<MapsMenu>
{
    protected override void Configure(IObjectTypeDescriptor<MapsMenu> descriptor)
    {
        descriptor.Name("MapsMenu");
        descriptor.Description("Menu information for restaurants");

        descriptor.Field(f => f.Link)
            .Description("Link to the menu");

        descriptor.Field(f => f.Source)
            .Description("Source of the menu data");
    }
}

public class MapsImageType : ObjectType<MapsImage>
{
    protected override void Configure(IObjectTypeDescriptor<MapsImage> descriptor)
    {
        descriptor.Name("MapsImage");
        descriptor.Description("Image information");

        descriptor.Field(f => f.Title)
            .Description("Image category or title");

        descriptor.Field(f => f.Thumbnail)
            .Description("Thumbnail URL");
    }
}

public class MapsUserReviewsType : ObjectType<MapsUserReviews>
{
    protected override void Configure(IObjectTypeDescriptor<MapsUserReviews> descriptor)
    {
        descriptor.Name("MapsUserReviews");
        descriptor.Description("User reviews collection");

        descriptor.Field(f => f.Summary)
            .Type<ListType<MapsReviewSummaryType>>()
            .Description("Summary snippets from reviews");

        descriptor.Field(f => f.MostRelevant)
            .Type<ListType<MapsReviewType>>()
            .Description("Most relevant detailed reviews");
    }
}

public class MapsReviewSummaryType : ObjectType<MapsReviewSummary>
{
    protected override void Configure(IObjectTypeDescriptor<MapsReviewSummary> descriptor)
    {
        descriptor.Name("MapsReviewSummary");
        descriptor.Description("Review summary snippet");

        descriptor.Field(f => f.Snippet)
            .Description("Quote or snippet from review");
    }
}

public class MapsReviewType : ObjectType<MapsReview>
{
    protected override void Configure(IObjectTypeDescriptor<MapsReview> descriptor)
    {
        descriptor.Name("MapsReview");
        descriptor.Description("Detailed user review");

        descriptor.Field(f => f.Username)
            .Description("Reviewer's username");

        descriptor.Field(f => f.Rating)
            .Description("Rating given (1-5 stars)");

        descriptor.Field(f => f.ContributorId)
            .Description("Google contributor ID");

        descriptor.Field(f => f.Description)
            .Type<AnyType>()
            .Description("Full review text");

        descriptor.Field(f => f.Images)
            .Type<ListType<MapsImageType>>()
            .Description("Images attached to the review");

        descriptor.Field(f => f.Date)
            .Description("When the review was posted");
    }
}

public class MapsRelatedSearchType : ObjectType<MapsRelatedSearch>
{
    protected override void Configure(IObjectTypeDescriptor<MapsRelatedSearch> descriptor)
    {
        descriptor.Name("MapsRelatedSearch");
        descriptor.Description("Related search suggestions");

        descriptor.Field(f => f.SearchTerm)
            .Description("Search term that people also use");

        descriptor.Field(f => f.LocalResults)
            .Type<ListType<LocalPlaceResultType>>()
            .Description("Local results for this search term");
    }
}

public class MapsPopularTimesType : ObjectType<MapsPopularTimes>
{
    protected override void Configure(IObjectTypeDescriptor<MapsPopularTimes> descriptor)
    {
        descriptor.Name("MapsPopularTimes");
        descriptor.Description("Popular times and busy periods");

        descriptor.Field(f => f.GraphResults)
            .Type<StringType>()
            .Description("Hourly busyness data by day of week (JSON format)");

        descriptor.Field(f => f.LiveHash)
            .Type<MapsLiveHashType>()
            .Description("Current live busyness information");
    }
}

public class MapsLiveHashType : ObjectType<MapsLiveHash>
{
    protected override void Configure(IObjectTypeDescriptor<MapsLiveHash> descriptor)
    {
        descriptor.Name("MapsLiveHash");
        descriptor.Description("Live busyness information");

        descriptor.Field(f => f.Info)
            .Description("Current busyness description");

        descriptor.Field(f => f.TimeSpent)
            .Description("Typical time spent at location");
    }
}

public class MapsEventType : ObjectType<MapsEvent>
{
    protected override void Configure(IObjectTypeDescriptor<MapsEvent> descriptor)
    {
        descriptor.Name("MapsEvent");
        descriptor.Description("Event information");

        descriptor.Field(f => f.Title)
            .Description("Event title");

        descriptor.Field(f => f.Date)
            .Type<MapsEventDateType>()
            .Description("Event date and time information");

        descriptor.Field(f => f.Thumbnail)
            .Description("Event thumbnail image");

        descriptor.Field(f => f.TicketInfo)
            .Type<MapsTicketInfoType>()
            .Description("Ticket purchasing information");
    }
}

public class MapsEventDateType : ObjectType<MapsEventDate>
{
    protected override void Configure(IObjectTypeDescriptor<MapsEventDate> descriptor)
    {
        descriptor.Name("MapsEventDate");
        descriptor.Description("Event date information");

        descriptor.Field(f => f.StartDate)
            .Description("Start date");

        descriptor.Field(f => f.StartTime)
            .Description("Start time");

        descriptor.Field(f => f.When)
            .Description("Combined date and time string");
    }
}

public class MapsTicketInfoType : ObjectType<MapsTicketInfo>
{
    protected override void Configure(IObjectTypeDescriptor<MapsTicketInfo> descriptor)
    {
        descriptor.Name("MapsTicketInfo");
        descriptor.Description("Ticket information");

        descriptor.Field(f => f.Price)
            .Description("Ticket price");

        descriptor.Field(f => f.ExtractedPrice)
            .Description("Numeric price value");

        descriptor.Field(f => f.Link)
            .Description("Link to purchase tickets");

        descriptor.Field(f => f.Source)
            .Description("Ticket source (e.g., 'Ticketmaster')");

        descriptor.Field(f => f.SourceIcon)
            .Description("Icon for ticket source");
    }
}

public class MapsQAType : ObjectType<MapsQA>
{
    protected override void Configure(IObjectTypeDescriptor<MapsQA> descriptor)
    {
        descriptor.Name("MapsQA");
        descriptor.Description("Question and answer pair");

        descriptor.Field(f => f.Question)
            .Type<MapsQuestionType>()
            .Description("User question");

        descriptor.Field(f => f.Answer)
            .Type<MapsAnswerType>()
            .Description("Business or user answer");

        descriptor.Field(f => f.TotalAnswers)
            .Description("Total number of answers to this question");
    }
}

public class MapsQuestionType : ObjectType<MapsQuestion>
{
    protected override void Configure(IObjectTypeDescriptor<MapsQuestion> descriptor)
    {
        descriptor.Name("MapsQuestion");
        descriptor.Description("User question");

        descriptor.Field(f => f.User)
            .Type<MapsUserType>()
            .Description("User who asked the question");

        descriptor.Field(f => f.Text)
            .Description("Question text");

        descriptor.Field(f => f.Date)
            .Description("When question was asked");

        descriptor.Field(f => f.Language)
            .Description("Language of the question");
    }
}

public class MapsAnswerType : ObjectType<MapsAnswer>
{
    protected override void Configure(IObjectTypeDescriptor<MapsAnswer> descriptor)
    {
        descriptor.Name("MapsAnswer");
        descriptor.Description("Answer to a question");

        descriptor.Field(f => f.User)
            .Type<MapsUserType>()
            .Description("User who provided the answer");

        descriptor.Field(f => f.Text)
            .Description("Answer text");

        descriptor.Field(f => f.Date)
            .Description("When answer was provided");

        descriptor.Field(f => f.Language)
            .Description("Language of the answer");
    }
}

public class MapsUserType : ObjectType<MapsUser>
{
    protected override void Configure(IObjectTypeDescriptor<MapsUser> descriptor)
    {
        descriptor.Name("MapsUser");
        descriptor.Description("User information");

        descriptor.Field(f => f.Name)
            .Description("User's display name");

        descriptor.Field(f => f.Link)
            .Description("Link to user profile");

        descriptor.Field(f => f.LocalGuideLevel)
            .Description("Google Local Guide level");

        descriptor.Field(f => f.Thumbnail)
            .Description("User's profile picture");
    }
}

public class MapsAtThisPlaceType : ObjectType<MapsAtThisPlace>
{
    protected override void Configure(IObjectTypeDescriptor<MapsAtThisPlace> descriptor)
    {
        descriptor.Name("MapsAtThisPlace");
        descriptor.Description("Other businesses at this location");

        descriptor.Field(f => f.Type)
            .Type<ListType<MapsPlaceTypeType>>()
            .Description("Categories of businesses");

        descriptor.Field(f => f.Places)
            .Type<ListType<MapsSubPlaceType>>()
            .Description("List of businesses");
    }
}

public class MapsPlaceTypeType : ObjectType<MapsPlaceType>
{
    protected override void Configure(IObjectTypeDescriptor<MapsPlaceType> descriptor)
    {
        descriptor.Name("MapsPlaceType");
        descriptor.Description("Business category information");

        descriptor.Field(f => f.Title)
            .Description("Category name");

        descriptor.Field(f => f.Places)
            .Description("Number of places in this category");
    }
}

public class MapsSubPlaceType : ObjectType<MapsSubPlace>
{
    protected override void Configure(IObjectTypeDescriptor<MapsSubPlace> descriptor)
    {
        descriptor.Name("MapsSubPlace");
        descriptor.Description("Business within a location");

        descriptor.Field(f => f.Position)
            .Description("Position in list");

        descriptor.Field(f => f.Title)
            .Description("Business name");

        descriptor.Field(f => f.DataId)
            .Description("Data ID");

        descriptor.Field(f => f.DataCid)
            .Description("CID");

        descriptor.Field(f => f.ReviewsLink)
            .Description("Reviews link");

        descriptor.Field(f => f.PhotosLink)
            .Description("Photos link");

        descriptor.Field(f => f.GpsCoordinates)
            .Type<GpsCoordinatesType>()
            .Description("GPS coordinates");

        descriptor.Field(f => f.PlaceIdSearch)
            .Description("Place ID search link");

        descriptor.Field(f => f.Rating)
            .Description("Rating");

        descriptor.Field(f => f.Reviews)
            .Description("Number of reviews");

        descriptor.Field(f => f.Type)
            .Description("Business type");

        descriptor.Field(f => f.TypeId)
            .Description("Business type ID");

        descriptor.Field(f => f.Address)
            .Description("Address");

        descriptor.Field(f => f.Location)
            .Description("Specific location within building");

        descriptor.Field(f => f.OpenState)
            .Description("Open/closed status");

        descriptor.Field(f => f.Hours)
            .Type<AnyType>()
            .Description("Operating hours as a human-readable string");

        descriptor.Field(f => f.Thumbnail)
            .Description("Thumbnail image");
    }
}

public class MapsAdmissionType : ObjectType<MapsAdmission>
{
    protected override void Configure(IObjectTypeDescriptor<MapsAdmission> descriptor)
    {
        descriptor.Name("MapsAdmission");
        descriptor.Description("Admission and ticket information");

        descriptor.Field(f => f.Title)
            .Description("Source name");

        descriptor.Field(f => f.Icon)
            .Description("Source icon");

        descriptor.Field(f => f.Options)
            .Type<ListType<MapsAdmissionOptionType>>()
            .Description("Available ticket options");
    }
}

public class MapsAdmissionOptionType : ObjectType<MapsAdmissionOption>
{
    protected override void Configure(IObjectTypeDescriptor<MapsAdmissionOption> descriptor)
    {
        descriptor.Name("MapsAdmissionOption");
        descriptor.Description("Specific admission option");

        descriptor.Field(f => f.Title)
            .Description("Ticket type or description");

        descriptor.Field(f => f.Link)
            .Description("Purchase link");

        descriptor.Field(f => f.Price)
            .Description("Price");

        descriptor.Field(f => f.ExtractedPrice)
            .Description("Numeric price");

        descriptor.Field(f => f.OfficialSite)
            .Description("Whether this is the official site");

        descriptor.Field(f => f.Extensions)
            .Type<ListType<StringType>>()
            .Description("Additional features (instant confirmation, mobile ticket, etc.)");
    }
}

public class MapsExperienceType : ObjectType<MapsExperience>
{
    protected override void Configure(IObjectTypeDescriptor<MapsExperience> descriptor)
    {
        descriptor.Name("MapsExperience");
        descriptor.Description("Experience or tour offering");

        descriptor.Field(f => f.Title)
            .Description("Experience title");

        descriptor.Field(f => f.Link)
            .Description("Booking link");

        descriptor.Field(f => f.Price)
            .Description("Price");

        descriptor.Field(f => f.ExtractedPrice)
            .Description("Numeric price");

        descriptor.Field(f => f.Rating)
            .Description("Rating");

        descriptor.Field(f => f.Reviews)
            .Description("Number of reviews");

        descriptor.Field(f => f.Thumbnail)
            .Description("Thumbnail image");

        descriptor.Field(f => f.SerpapiThumbnail)
            .Description("SerpApi thumbnail");

        descriptor.Field(f => f.Source)
            .Description("Source provider");

        descriptor.Field(f => f.Icon)
            .Description("Source icon");

        descriptor.Field(f => f.Duration)
            .Description("Duration");
    }
}

public class MapsPostType : ObjectType<MapsPost>
{
    protected override void Configure(IObjectTypeDescriptor<MapsPost> descriptor)
    {
        descriptor.Name("MapsPost");
        descriptor.Description("Business post");

        descriptor.Field(f => f.Title)
            .Description("Post title");

        descriptor.Field(f => f.Media)
            .Description("Media URL (image/video)");

        descriptor.Field(f => f.Cta)
            .Description("Call to action text");

        descriptor.Field(f => f.Link)
            .Description("Action link");

        descriptor.Field(f => f.Phone)
            .Description("Phone number for calls");

        descriptor.Field(f => f.PostLink)
            .Description("Link to full post");

        descriptor.Field(f => f.Description)
            .Description("Post description");

        descriptor.Field(f => f.Duration)
            .Description("Offer duration");

        descriptor.Field(f => f.Date)
            .Description("Post date");
    }
}

public class MapsWeatherType : ObjectType<MapsWeather>
{
    protected override void Configure(IObjectTypeDescriptor<MapsWeather> descriptor)
    {
        descriptor.Name("MapsWeather");
        descriptor.Description("Weather information");

        descriptor.Field(f => f.Celsius)
            .Description("Temperature in Celsius");

        descriptor.Field(f => f.Fahrenheit)
            .Description("Temperature in Fahrenheit");

        descriptor.Field(f => f.Conditions)
            .Description("Weather conditions");
    }
}

public class MapsAtLocationType : ObjectType<MapsAtLocation>
{
    protected override void Configure(IObjectTypeDescriptor<MapsAtLocation> descriptor)
    {
        descriptor.Name("MapsAtLocation");
        descriptor.Description("Place at this location");

        descriptor.Field(f => f.Position)
            .Description("Position in list");

        descriptor.Field(f => f.Title)
            .Description("Place name");

        descriptor.Field(f => f.DataId)
            .Description("Data ID");

        descriptor.Field(f => f.DataCid)
            .Description("CID");

        descriptor.Field(f => f.ReviewsLink)
            .Description("Reviews link");

        descriptor.Field(f => f.PhotosLink)
            .Description("Photos link");

        descriptor.Field(f => f.GpsCoordinates)
            .Type<GpsCoordinatesType>()
            .Description("GPS coordinates");

        descriptor.Field(f => f.PlaceIdSearch)
            .Description("Place ID search link");

        descriptor.Field(f => f.Rating)
            .Description("Rating");

        descriptor.Field(f => f.Type)
            .Description("Place type");

        descriptor.Field(f => f.Price)
            .Description("Price level");

        descriptor.Field(f => f.Thumbnail)
            .Description("Thumbnail image");
    }
}

