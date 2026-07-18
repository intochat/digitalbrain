using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class OpenTableReviewsType : ObjectType<GetOpenTableReviewsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetOpenTableReviewsResponse> descriptor)
    {
        descriptor.Description("Represents an OpenTable reviews response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<OpenTableSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<OpenTableSearchParametersType>()
            .Description("Parameters used for the OpenTable reviews search.");

        descriptor.Field(f => f.SearchInformation)
            .Type<OpenTableSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.ReviewsSummary)
            .Type<OpenTableReviewsSummaryType>()
            .Description("Summary statistics for reviews and ratings.");

        descriptor.Field(f => f.Awards)
            .Type<ListType<OpenTableAwardType>>()
            .Description("Awards for the restaurant.");

        descriptor.Field(f => f.Reviews)
            .Type<ListType<OpenTableReviewType>>()
            .Description("List of OpenTable reviews.");

        descriptor.Field(f => f.SerpApiPagination)
            .Type<OpenTableSerpApiPaginationType>()
            .Description("SerpApi pagination links.");

        descriptor.Field(f => f.Error)
            .Type<StringType>()
            .Description("Error message when the request fails.");
    }
}

public class OpenTableSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("OpenTableSearchMetadata");
        descriptor.Description("Metadata information about the OpenTable reviews request.");

        descriptor.Field(m => m.Id).Type<StringType>();
        descriptor.Field(m => m.Status).Type<StringType>();
        descriptor.Field(m => m.JsonEndpoint).Type<StringType>();
        descriptor.Field(m => m.CreatedAt).Type<StringType>();
        descriptor.Field(m => m.ProcessedAt).Type<StringType>();
        descriptor.Field(m => m.RawHtmlFile).Type<StringType>();
        descriptor.Field(m => m.PrettifyHtmlFile).Type<StringType>();
        descriptor.Field(m => m.OpenTableReviewsUrl).Type<StringType>();
        descriptor.Field(m => m.TotalTimeTaken).Type<FloatType>();
    }
}

public class OpenTableSearchParametersType : ObjectType<OpenTableSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for the OpenTable reviews search.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.Rid).Type<StringType>();
        descriptor.Field(p => p.OpenTableDomain).Type<StringType>();
        descriptor.Field(p => p.Page).Type<StringType>();
    }
}

public class OpenTableSearchInformationType : ObjectType<OpenTableSearchInformation>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableSearchInformation> descriptor)
    {
        descriptor.Description("Search information for OpenTable reviews.");

        descriptor.Field(p => p.Page).Type<IntType>();
        descriptor.Field(p => p.TotalPages).Type<IntType>();
    }
}

public class OpenTableReviewsSummaryType : ObjectType<OpenTableReviewsSummary>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewsSummary> descriptor)
    {
        descriptor.Description("Summary information for OpenTable reviews.");

        descriptor.Field(s => s.ReviewsCount).Type<IntType>();
        descriptor.Field(s => s.RatingsCount).Type<IntType>();
        descriptor.Field(s => s.RatingsSummary).Type<OpenTableRatingsSummaryType>();
        descriptor.Field(s => s.Ratings).Type<ListType<OpenTableRatingBreakdownType>>();
        descriptor.Field(s => s.AiSummary).Type<StringType>();
    }
}

public class OpenTableRatingsSummaryType : ObjectType<OpenTableRatingsSummary>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableRatingsSummary> descriptor)
    {
        descriptor.Description("Aggregate rating summary.");

        descriptor.Field(r => r.Overall).Type<FloatType>();
        descriptor.Field(r => r.Food).Type<FloatType>();
        descriptor.Field(r => r.Service).Type<FloatType>();
        descriptor.Field(r => r.Ambience).Type<FloatType>();
        descriptor.Field(r => r.Value).Type<FloatType>();
        descriptor.Field(r => r.Noise).Type<StringType>();
    }
}

public class OpenTableRatingBreakdownType : ObjectType<OpenTableRatingBreakdown>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableRatingBreakdown> descriptor)
    {
        descriptor.Description("Rating distribution by stars.");

        descriptor.Field(r => r.Stars).Type<IntType>();
        descriptor.Field(r => r.Count).Type<IntType>();
    }
}

public class OpenTableAwardType : ObjectType<OpenTableAward>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableAward> descriptor)
    {
        descriptor.Description("Award data for the restaurant.");

        descriptor.Field(a => a.Location).Type<StringType>();
        descriptor.Field(a => a.Name).Type<StringType>();
    }
}

public class OpenTableReviewType : ObjectType<OpenTableReview>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReview> descriptor)
    {
        descriptor.Description("OpenTable review entry.");

        descriptor.Field(r => r.Id).Type<StringType>();
        descriptor.Field(r => r.Content).Type<StringType>().Resolve(context => OpenTableReviewTextNormalizer.Normalize(context.Parent<OpenTableReview>().Content));
        descriptor.Field(r => r.SubmittedAt).Type<StringType>();
        descriptor.Field(r => r.DinedAt).Type<StringType>();
        descriptor.Field(r => r.Rating).Type<OpenTableReviewRatingsType>();
        descriptor.Field(r => r.Ratings).Type<OpenTableReviewRatingsType>();
        descriptor.Field(r => r.User).Type<OpenTableReviewUserType>();
        descriptor.Field(r => r.Helpfulness).Type<OpenTableReviewHelpfulnessType>();
        descriptor.Field(r => r.Images).Type<ListType<OpenTableReviewImageType>>();
        descriptor.Field(r => r.Response).Type<OpenTableReviewResponseType>();
    }
}

public class OpenTableReviewRatingsType : ObjectType<OpenTableReviewRatings>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewRatings> descriptor)
    {
        descriptor.Description("Ratings for a review.");

        descriptor.Field(r => r.Overall).Type<IntType>();
        descriptor.Field(r => r.Food).Type<IntType>();
        descriptor.Field(r => r.Service).Type<IntType>();
        descriptor.Field(r => r.Ambience).Type<IntType>();
        descriptor.Field(r => r.Value).Type<IntType>();
        descriptor.Field(r => r.Noise).Type<StringType>();
    }
}

public class OpenTableReviewUserType : ObjectType<OpenTableReviewUser>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewUser> descriptor)
    {
        descriptor.Description("User information for a review.");

        descriptor.Field(u => u.Name).Type<StringType>();
        descriptor.Field(u => u.NumberOfReviews).Type<IntType>();
        descriptor.Field(u => u.Location).Type<StringType>();
        descriptor.Field(u => u.Avatar).Type<StringType>();
        descriptor.Field(u => u.Vip).Type<BooleanType>();
    }
}

public class OpenTableReviewHelpfulnessType : ObjectType<OpenTableReviewHelpfulness>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewHelpfulness> descriptor)
    {
        descriptor.Description("Helpfulness metrics for a review.");

        descriptor.Field(h => h.Up).Type<IntType>();
        descriptor.Field(h => h.Score).Type<IntType>();
    }
}

public class OpenTableReviewImageType : ObjectType<OpenTableReviewImage>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewImage> descriptor)
    {
        descriptor.Description("Image attached to a review.");

        descriptor.Field(i => i.Id).Type<StringType>();
        descriptor.Field(i => i.Timestamp).Type<StringType>();
        descriptor.Field(i => i.Variants).Type<ListType<OpenTableReviewImageVariantType>>();
    }
}

public class OpenTableReviewImageVariantType : ObjectType<OpenTableReviewImageVariant>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewImageVariant> descriptor)
    {
        descriptor.Description("Image variant for a review image.");

        descriptor.Field(v => v.Size).Type<StringType>();
        descriptor.Field(v => v.Url).Type<StringType>();
    }
}

public class OpenTableReviewResponseType : ObjectType<OpenTableReviewResponse>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableReviewResponse> descriptor)
    {
        descriptor.Description("Owner response to a review.");

        descriptor.Field(r => r.Content)
            .Type<StringType>()
            .Resolve(context => OpenTableReviewTextNormalizer.Normalize(context.Parent<OpenTableReviewResponse>().Content));
        descriptor.Field(r => r.Date).Type<StringType>();
    }
}

public class OpenTableSerpApiPaginationType : ObjectType<OpenTableSerpApiPagination>
{
    protected override void Configure(IObjectTypeDescriptor<OpenTableSerpApiPagination> descriptor)
    {
        descriptor.Description("SerpApi pagination links.");

        descriptor.Field(p => p.Previous).Type<StringType>();
        descriptor.Field(p => p.Next).Type<StringType>();
    }
}

public class OpenTableReviewsInputType : InputObjectType<GetOpenTableReviewsRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetOpenTableReviewsRequest> descriptor)
    {
        descriptor.Name("GetOpenTableReviewsRequest");
        descriptor.Description("Input parameters for OpenTable reviews.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.Rid)
            .Type<NonNullType<StringType>>()
            .Description("OpenTable restaurant id extracted from the URL path.");

        descriptor.Field(f => f.OpenTableDomain)
            .Type<StringType>()
            .Description("OpenTable domain (e.g., opentable.com). Optional.");

        descriptor.Field(f => f.Page)
            .Type<IntType>()
            .Description("Page number for pagination (10 results per page). Optional.");
    }
}



static file class OpenTableReviewTextNormalizer
{
    public static string Normalize(string? value) => value ?? string.Empty;
}
