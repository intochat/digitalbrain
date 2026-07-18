using System.Globalization;
using System.Text;
using System.Text.Json;
using HotChocolate.Resolvers;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class PlaceReviewsType : ObjectType<GetPlaceReviewsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetPlaceReviewsResponse> descriptor)
    {
        descriptor.Description(
            "Represents a complete place reviews response with metadata, search parameters, and review results");

        descriptor.Field(f => f.SearchMetadata)
            .Type<PlaceReviewsSearchMetadataType>()
            .Description("Metadata about the search request such as status and processing times");

        descriptor.Field(f => f.SearchParameters)
            .Type<PlaceReviewsSearchParametersType>()
            .Description("Parameters used for the place reviews search");

        descriptor.Field(f => f.PlaceInfo)
            .Type<PlaceReviewsPlaceInfoType>()
            .Description("Information about the place being reviewed");

        descriptor.Field(f => f.Topics)
            .Type<ListType<PlaceReviewsTopicType>>()
            .Description("List of review topics/categories available for this place");

        descriptor.Field(f => f.Reviews)
            .Type<ListType<PlaceReviewType>>()
            .Description("List of reviews for the place");

        descriptor.Field(f => f.Pagination)
            .Type<PlaceReviewsPaginationResultType>()
            .Description("Pagination information for browsing through review results");
    }
}

public class PlaceReviewsSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("PlaceReviewsSearchMetadata");
        descriptor.Description("Metadata information about the place reviews search request");

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

public class PlaceReviewsSearchParametersType : ObjectType<PlaceReviewsSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for the place reviews search query");

        descriptor.Field(p => p.Engine)
            .Type<StringType>()
            .Description("Search engine used");

        descriptor.Field(p => p.PlaceId)
            .Type<StringType>()
            .Description("Google Place ID for the place being reviewed");

        descriptor.Field(p => p.DataId)
            .Type<StringType>()
            .Description("Alternative data identifier for the place");

        descriptor.Field(p => p.SortBy)
            .Type<StringType>()
            .Description("Sorting method for reviews (qualityScore, newestFirst, ratingHigh, ratingLow)");

        descriptor.Field(p => p.TopicId)
            .Type<StringType>()
            .Description("Topic filter applied to reviews");

        descriptor.Field(p => p.Hl)
            .Type<StringType>()
            .Description("Language parameter for the search");

        descriptor.Field(p => p.Num)
            .Type<IntType>()
            .Description("Number of reviews requested");

        descriptor.Field(p => p.NextPageToken)
            .Type<StringType>()
            .Description("Token for accessing the next page of results");
    }
}

public class PlaceReviewsPlaceInfoType : ObjectType<PlaceReviewsPlaceInfo>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsPlaceInfo> descriptor)
    {
        descriptor.Description("Information about the place being reviewed");

        descriptor.Field(p => p.Title)
            .Type<StringType>()
            .Description("Name of the place");

        descriptor.Field(p => p.DataId)
            .Type<StringType>()
            .Description("Data identifier for the place");

        descriptor.Field(p => p.DataCid)
            .Type<StringType>()
            .Description("CID identifier for the place");

        descriptor.Field(p => p.ReviewsLink)
            .Type<StringType>()
            .Description("Link to all reviews for this place");

        descriptor.Field(p => p.PhotosLink)
            .Type<StringType>()
            .Description("Link to photos for this place");

        descriptor.Field(p => p.GpsCoordinates)
            .Type<GpsCoordinatesType>()
            .Description("GPS coordinates of the place");

        descriptor.Field(p => p.PlaceId)
            .Type<StringType>()
            .Description("Google Place ID");

        descriptor.Field(p => p.ReviewsId)
            .Type<StringType>()
            .Description("Reviews-specific identifier");

        descriptor.Field(p => p.LocatedIn)
            .Type<StringType>()
            .Description("Location context for the place");

        descriptor.Field(p => p.Rating)
            .Type<FloatType>()
            .Description("Overall rating of the place (0-5)");

        descriptor.Field(p => p.Reviews)
            .Type<IntType>()
            .Description("Total number of reviews for this place");

        descriptor.Field(p => p.Type)
            .Type<StringType>()
            .Description("Primary type/category of the place");

        descriptor.Field(p => p.Types)
            .Type<ListType<StringType>>()
            .Description("All types/categories applicable to this place");

        descriptor.Field(p => p.Address)
            .Type<StringType>()
            .Description("Physical address of the place");

        descriptor.Field(p => p.OpenState)
            .Type<StringType>()
            .Description("Current open/closed status");

        descriptor.Field(p => p.Hours)
            .Type<StringType>()
            .Description("Operating hours summary");

        descriptor.Field(p => p.OperatingHours)
            .Type<PlaceReviewsOperatingHoursType>()
            .Description("Detailed operating hours breakdown");

        descriptor.Field(p => p.Phone)
            .Type<StringType>()
            .Description("Contact phone number");

        descriptor.Field(p => p.Website)
            .Type<StringType>()
            .Description("Website URL");

        descriptor.Field(p => p.Description)
            .Type<StringType>()
            .Description("Description of the place");

        descriptor.Field(p => p.Price)
            .Type<StringType>()
            .Description("Price level or range");

        descriptor.Field(p => p.EditorialSummary)
            .Type<PlaceReviewsEditorialSummaryType>()
            .Description("Editorial summary information");

        descriptor.Field(p => p.UserReview)
            .Type<PlaceReviewsUserReviewType>()
            .Description("User review preview");
    }
}

public class PlaceReviewsTopicType : ObjectType<PlaceReviewsTopic>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsTopic> descriptor)
    {
        descriptor.Description("Review topic/category with mention information");

        descriptor.Field(t => t.Keyword)
            .Type<StringType>()
            .Description("Topic keyword");

        descriptor.Field(t => t.Mentions)
            .Type<IntType>()
            .Description("Number of mentions for this topic");

        descriptor.Field(t => t.Id)
            .Type<StringType>()
            .Description("Unique identifier for the topic");
    }
}

public class PlaceReviewType : ObjectType<PlaceReview>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReview> descriptor)
    {
        descriptor.Description("Individual review with user information and content");

        descriptor.Field(r => r.Link)
            .Type<StringType>()
            .Description("Direct link to the review");

        descriptor.Field(r => r.Position)
            .Type<IntType>()
            .Description("Position of this review in the results");

        descriptor.Field(r => r.User)
            .Type<PlaceReviewsUserType>()
            .Description("Information about the reviewer");

        descriptor.Field(r => r.Rating)
            .Type<FloatType>()
            .Description("Star rating given by the reviewer (0-5, supports decimal values)");

        descriptor.Field(r => r.Date)
            .Type<StringType>()
            .Description("Date when the review was posted");

        descriptor.Field(r => r.IsoDate)
            .Type<StringType>()
            .Description("ISO formatted date of the review");

        descriptor.Field(r => r.IsoDateOfLastEdit)
            .Type<StringType>()
            .Description("ISO formatted date of last edit");

        descriptor.Field(r => r.Snippet)
            .Type<StringType>()
            .Resolve(context => PlaceReviewsGraphQlStringHelper.ResolveStringField(context, nameof(PlaceReview.Snippet), "snippet"))
            .Description("Review text content");

        descriptor.Field(r => r.ExtractedSnippet)
            .Type<PlaceReviewsExtractedSnippetType>()
            .Description("Extracted snippet information");

        descriptor.Field(r => r.Likes)
            .Type<IntType>()
            .Description("Number of likes the review received");

        descriptor.Field(r => r.Images)
            .Type<ListType<StringType>>()
            .Description("List of image URLs attached to the review");

        descriptor.Field(r => r.Source)
            .Type<StringType>()
            .Description("Source platform of the review");

        descriptor.Field(r => r.ReviewId)
            .Type<StringType>()
            .Description("Unique identifier for the review");

        descriptor.Field(r => r.LocalGuide)
            .Type<BooleanType>()
            .Description("Whether the reviewer is a local guide");

        descriptor.Field(r => r.Details)
            .Type<PlaceReviewsDetailsType>()
            .Description("Additional review details and ratings");

        descriptor.Field(r => r.Response)
            .Type<PlaceReviewsOwnerResponseType>()
            .Description("Response from the business owner");

        descriptor.Field(r => r.ResponseFromOwnerText)
            .Type<StringType>()
            .Description("Text response from the business owner");

        descriptor.Field(r => r.ResponseFromOwnerAgo)
            .Type<StringType>()
            .Description("Time since owner response was posted");
    }
}

public class PlaceReviewsOperatingHoursType : ObjectType<PlaceReviewsOperatingHours>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsOperatingHours> descriptor)
    {
        descriptor.Description("Weekly operating hours for the place");

        descriptor.Field(h => h.Monday).Type<StringType>().Description("Monday operating hours");
        descriptor.Field(h => h.Tuesday).Type<StringType>().Description("Tuesday operating hours");
        descriptor.Field(h => h.Wednesday).Type<StringType>().Description("Wednesday operating hours");
        descriptor.Field(h => h.Thursday).Type<StringType>().Description("Thursday operating hours");
        descriptor.Field(h => h.Friday).Type<StringType>().Description("Friday operating hours");
        descriptor.Field(h => h.Saturday).Type<StringType>().Description("Saturday operating hours");
        descriptor.Field(h => h.Sunday).Type<StringType>().Description("Sunday operating hours");
    }
}

public class PlaceReviewsEditorialSummaryType : ObjectType<PlaceReviewsEditorialSummary>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsEditorialSummary> descriptor)
    {
        descriptor.Description("Editorial summary of the place");

        descriptor.Field(e => e.Overview)
            .Type<StringType>()
            .Description("Editorial overview text");
    }
}

public class PlaceReviewsUserReviewType : ObjectType<PlaceReviewsUserReview>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsUserReview> descriptor)
    {
        descriptor.Description("User review summary information");

        descriptor.Field(u => u.Rating)
            .Type<FloatType>()
            .Description("User's rating (0-5)");

        descriptor.Field(u => u.Snippet)
            .Type<StringType>()
            .Resolve(context => PlaceReviewsGraphQlStringHelper.ResolveStringField(context, nameof(PlaceReviewsUserReview.Snippet), "snippet"))
            .Description("User review snippet");
    }
}

public class PlaceReviewsUserType : ObjectType<PlaceReviewsUser>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsUser> descriptor)
    {
        descriptor.Description("Information about a reviewer");

        descriptor.Field(u => u.Name)
            .Type<StringType>()
            .Description("Reviewer's display name");

        descriptor.Field(u => u.Link)
            .Type<StringType>()
            .Description("Link to reviewer's profile");

        descriptor.Field(u => u.ContributorId)
            .Type<StringType>()
            .Description("Reviewer's contributor ID");

        descriptor.Field(u => u.Thumbnail)
            .Type<StringType>()
            .Description("URL to reviewer's profile picture");

        descriptor.Field(u => u.LocalGuide)
            .Type<BooleanType>()
            .Description("Whether the reviewer is a local guide");

        descriptor.Field(u => u.Reviews)
            .Type<IntType>()
            .Description("Total number of reviews by this user");

        descriptor.Field(u => u.Photos)
            .Type<IntType>()
            .Description("Total number of photos contributed by this user");
    }
}

public class PlaceReviewsExtractedSnippetType : ObjectType<PlaceReviewsExtractedSnippet>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsExtractedSnippet> descriptor)
    {
        descriptor.Description("Extracted snippet information");

        descriptor.Field(e => e.Original)
            .Type<StringType>()
            .Resolve(context => PlaceReviewsGraphQlStringHelper.ResolveStringField(context, nameof(PlaceReviewsExtractedSnippet.Original), "original"))
            .Description("Original extracted text");
    }
}

public class PlaceReviewsDetailsType : ObjectType<PlaceReviewsDetails>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsDetails> descriptor)
    {
        descriptor.Description("Detailed review information with specific ratings");

        descriptor.Field(d => d.Service)
            .Type<StringType>()
            .Description("Service rating or comment");

        descriptor.Field(d => d.MealType)
            .Type<StringType>()
            .Description("Type of meal reviewed");

        descriptor.Field(d => d.PricePerPerson)
            .Type<StringType>()
            .Description("Price range per person");

        descriptor.Field(d => d.Food)
            .Type<StringType>()
            .Description("Food rating or comment");

        descriptor.Field(d => d.Atmosphere)
            .Type<StringType>()
            .Description("Atmosphere rating or comment");

        descriptor.Field(d => d.RecommendedDishes)
            .Type<StringType>()
            .Description("Recommended dishes");

        descriptor.Field(d => d.VegetarianOptions)
            .Type<StringType>()
            .Description("Vegetarian options availability");

        descriptor.Field(d => d.DietaryRestrictions)
            .Type<StringType>()
            .Description("Dietary restrictions accommodations");

        descriptor.Field(d => d.KidFriendliness)
            .Type<StringType>()
            .Description("Kid-friendly features");

        descriptor.Field(d => d.WheelchairAccessibility)
            .Type<StringType>()
            .Description("Wheelchair accessibility information");
    }
}

public class PlaceReviewsOwnerResponseType : ObjectType<PlaceReviewsOwnerResponse>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsOwnerResponse> descriptor)
    {
        descriptor.Description("Response from the business owner to a review");

        descriptor.Field(r => r.Date)
            .Type<StringType>()
            .Description("Date when the response was posted");

        descriptor.Field(r => r.IsoDate)
            .Type<StringType>()
            .Description("ISO formatted date of the response");

        descriptor.Field(r => r.IsoDateOfLastEdit)
            .Type<StringType>()
            .Description("ISO formatted date of last edit");

        descriptor.Field(r => r.Snippet)
            .Type<StringType>()
            .Resolve(context => PlaceReviewsGraphQlStringHelper.ResolveStringField(context, nameof(PlaceReviewsOwnerResponse.Snippet), "snippet"))
            .Description("Owner response text");

        descriptor.Field(r => r.ExtractedSnippet)
            .Type<PlaceReviewsExtractedSnippetType>()
            .Description("Extracted snippet from the response");
    }
}

internal static class PlaceReviewsGraphQlStringHelper
{
    public static string? ResolveStringField(IResolverContext context, params string[] candidateNames)
    {
        var value = GetValue(context.Parent<object>(), candidateNames);
        return CoerceToSafeString(value);
    }

    private static object? GetValue(object? source, string[] candidateNames)
    {
        if (source is null)
        {
            return null;
        }

        if (source is JsonElement jsonElement)
        {
            foreach (var candidateName in candidateNames)
            {
                if (TryGetJsonPropertyValue(jsonElement, candidateName, out var jsonValue))
                {
                    return jsonValue;
                }
            }

            return null;
        }

        if (source is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            foreach (var candidateName in candidateNames)
            {
                if (TryGetDictionaryValue(readOnlyDictionary, candidateName, out var dictionaryValue))
                {
                    return dictionaryValue;
                }
            }

            return null;
        }

        if (source is IDictionary<string, object?> dictionary)
        {
            foreach (var candidateName in candidateNames)
            {
                if (TryGetDictionaryValue(dictionary, candidateName, out var dictionaryValue))
                {
                    return dictionaryValue;
                }
            }

            return null;
        }

        var sourceType = source.GetType();
        foreach (var candidateName in candidateNames)
        {
            var property = sourceType.GetProperty(candidateName);
            if (property is not null)
            {
                return property.GetValue(source);
            }
        }

        return null;
    }

    private static bool TryGetJsonPropertyValue(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetDictionaryValue(
        IReadOnlyDictionary<string, object?> dictionary,
        string key,
        out object? value)
    {
        if (dictionary.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var item in dictionary)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = item.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetDictionaryValue(
        IDictionary<string, object?> dictionary,
        string key,
        out object? value)
    {
        if (dictionary.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var item in dictionary)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = item.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static string? CoerceToSafeString(object? value)
    {
        var normalized = value switch
        {
            null => null,
            string stringValue => stringValue,
            JsonElement jsonElement => jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                _ => jsonElement.ToString()
            },
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

        if (string.IsNullOrEmpty(normalized))
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++)
        {
            var current = normalized[i];

            if (char.IsControl(current) && current is not ('\n' or '\r' or '\t'))
            {
                continue;
            }

            if (char.IsHighSurrogate(current))
            {
                if (i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1]))
                {
                    builder.Append(current);
                    builder.Append(normalized[++i]);
                }

                continue;
            }

            if (char.IsLowSurrogate(current))
            {
                continue;
            }

            builder.Append(current);
        }

        return builder.Length == normalized.Length ? normalized : builder.ToString();
    }
}

public class PlaceReviewsPaginationResultType : ObjectType<PlaceReviewsPaginationResult>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsPaginationResult> descriptor)
    {
        descriptor.Description("Pagination information for review results");

        descriptor.Field(p => p.NextPageToken)
            .Type<StringType>()
            .Description("Token for accessing the next page of results");

        descriptor.Field(p => p.SerpApiPagination)
            .Type<PlaceReviewsSerpApiPaginationType>()
            .Description("SerpApi-specific pagination information");
    }
}

public class PlaceReviewsSerpApiPaginationType : ObjectType<PlaceReviewsSerpApiPagination>
{
    protected override void Configure(IObjectTypeDescriptor<PlaceReviewsSerpApiPagination> descriptor)
    {
        descriptor.Description("SerpApi-specific pagination information");

        descriptor.Field(p => p.Next)
            .Type<StringType>()
            .Description("Next page URL");

        descriptor.Field(p => p.NextPageToken)
            .Type<StringType>()
            .Description("Next page token");
    }
}

public class PlaceReviewsInputType : InputObjectType<GetPlaceReviewsRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetPlaceReviewsRequest> descriptor)
    {
        descriptor.Name("GetPlaceReviewsRequest");
        descriptor.Description("Input parameters for querying place reviews information");

        descriptor.Field(f => f.PlaceId)
            .Type<StringType>()
            .Description("Google Place ID for the place to get reviews for");

        descriptor.Field(f => f.DataId)
            .Type<StringType>()
            .Description("Alternative data identifier for the place");

        descriptor.Field(f => f.Localization)
            .Type<PlaceReviewsLocalizationInputType>()
            .Description("Localization options such as language");

        descriptor.Field(f => f.Filters)
            .Type<PlaceReviewsFiltersInputType>()
            .Description("Optional filters for refining review results");

        descriptor.Field(f => f.Pagination)
            .Type<PlaceReviewsPaginationInputType>()
            .Description("Pagination options for managing result sets");
    }
}

public class PlaceReviewsFiltersInputType : InputObjectType<PlaceReviewsFilters>
{
    protected override void Configure(IInputObjectTypeDescriptor<PlaceReviewsFilters> descriptor)
    {
        descriptor.Description("Filters for refining place reviews search results");

        descriptor.Field(f => f.SortBy)
            .Type<StringType>()
            .Description("Sort reviews by: qualityScore, newestFirst, ratingHigh, ratingLow");

        descriptor.Field(f => f.TopicId)
            .Type<StringType>()
            .Description("Filter reviews by topic (e.g., food, service, atmosphere)");
    }
}

public class PlaceReviewsPaginationInputType : InputObjectType<PlaceReviewsPagination>
{
    protected override void Configure(IInputObjectTypeDescriptor<PlaceReviewsPagination> descriptor)
    {
        descriptor.Description("Pagination options for place reviews results");

        descriptor.Field(p => p.Num)
            .Type<IntType>()
            .Description("Number of reviews to return (1-40)");

        descriptor.Field(p => p.NextPageToken)
            .Type<StringType>()
            .Description("Token for accessing the next page of results");
    }
}

public class PlaceReviewsLocalizationInputType : InputObjectType<Localization>
{
    protected override void Configure(IInputObjectTypeDescriptor<Localization> descriptor)
    {
        descriptor.Name("PlaceReviewsLocalization");
        descriptor.Description("Localization settings for the reviews search");

        descriptor.Field(l => l.Hl)
            .Type<StringType>()
            .Description("Language code (e.g., 'en', 'es', 'fr')");

        descriptor.Field(l => l.Gl)
            .Type<StringType>()
            .Description("Country code (e.g., 'US', 'CA', 'UK')");

        descriptor.Field(l => l.Currency)
            .Type<StringType>()
            .Description("Currency code (e.g., 'USD', 'EUR', 'GBP')");
    }
}

