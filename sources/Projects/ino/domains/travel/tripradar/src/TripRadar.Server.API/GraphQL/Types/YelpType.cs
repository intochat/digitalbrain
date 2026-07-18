using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class YelpSearchType : ObjectType<GetYelpSearchResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetYelpSearchResponse> descriptor)
    {
        descriptor.Description("Represents a Yelp search response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<YelpSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<YelpSearchParametersType>()
            .Description("Parameters used for Yelp search.");

        descriptor.Field(f => f.SearchInformation)
            .Type<YelpSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.Filters)
            .Type<AnyType>()
            .Description("Filter data (raw SerpApi object).");

        descriptor.Field(f => f.AdsResults)
            .Type<ListType<AnyType>>()
            .Description("Ads results (raw SerpApi objects).");

        descriptor.Field(f => f.OrganicResults)
            .Type<ListType<AnyType>>()
            .Description("Organic results (raw SerpApi objects).");

        descriptor.Field(f => f.Pagination)
            .Type<AnyType>()
            .Description("Pagination data (raw SerpApi object).");

        descriptor.Field(f => f.SerpApiPagination)
            .Type<AnyType>()
            .Description("SerpApi pagination details.");
    }
}

public class YelpPlaceType : ObjectType<GetYelpPlaceResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetYelpPlaceResponse> descriptor)
    {
        descriptor.Description("Represents a Yelp place response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<YelpSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<YelpPlaceSearchParametersType>()
            .Description("Parameters used for Yelp place.");

        descriptor.Field(f => f.SearchInformation)
            .Type<YelpSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.PlaceResults)
            .Type<AnyType>()
            .Description("Place results (raw SerpApi object).");

        descriptor.Field(f => f.Error)
            .Type<StringType>()
            .Description("Error details from SerpApi.");
    }
}

public class YelpPlaceFullMenuType : ObjectType<GetYelpPlaceFullMenuResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetYelpPlaceFullMenuResponse> descriptor)
    {
        descriptor.Description("Represents a Yelp full menu response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<YelpSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<YelpPlaceSearchParametersType>()
            .Description("Parameters used for Yelp place full menu.");

        descriptor.Field(f => f.SearchInformation)
            .Type<YelpSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.PlaceResults)
            .Type<AnyType>()
            .Description("Place results (raw SerpApi object).");

        descriptor.Field(f => f.FullMenuResults)
            .Type<AnyType>()
            .Description("Full menu results (raw SerpApi object).");

        descriptor.Field(f => f.Error)
            .Type<StringType>()
            .Description("Error details from SerpApi.");
    }
}

public class YelpReviewsType : ObjectType<GetYelpReviewsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetYelpReviewsResponse> descriptor)
    {
        descriptor.Description("Represents a Yelp reviews response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<YelpSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<YelpReviewsSearchParametersType>()
            .Description("Parameters used for Yelp reviews.");

        descriptor.Field(f => f.SearchInformation)
            .Type<YelpSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.ReviewLanguages)
            .Type<ListType<AnyType>>()
            .Description("Review languages (raw SerpApi objects).");

        descriptor.Field(f => f.Reviews)
            .Type<ListType<AnyType>>()
            .Description("Review results (raw SerpApi objects).");

        descriptor.Field(f => f.SerpApiPagination)
            .Type<AnyType>()
            .Description("SerpApi pagination details.");

        descriptor.Field(f => f.Error)
            .Type<StringType>()
            .Description("Error details from SerpApi.");
    }
}

public class YelpSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("YelpSearchMetadata");
        descriptor.Description("Metadata information about the Yelp request.");

        descriptor.Field(m => m.Id).Type<StringType>();
        descriptor.Field(m => m.Status).Type<StringType>();
        descriptor.Field(m => m.JsonEndpoint).Type<StringType>();
        descriptor.Field(m => m.CreatedAt).Type<StringType>();
        descriptor.Field(m => m.ProcessedAt).Type<StringType>();
        descriptor.Field(m => m.RawHtmlFile).Type<StringType>();
        descriptor.Field(m => m.PrettifyHtmlFile).Type<StringType>();
        descriptor.Field(m => m.YelpUrl).Type<StringType>();
        descriptor.Field(m => m.YelpPlaceUrl).Type<StringType>();
        descriptor.Field(m => m.YelpReviewsUrl).Type<StringType>();
        descriptor.Field(m => m.TotalTimeTaken).Type<FloatType>();
    }
}

public class YelpSearchInformationType : ObjectType<SearchInformation>
{
    protected override void Configure(IObjectTypeDescriptor<SearchInformation> descriptor)
    {
        descriptor.Name("YelpSearchInformation");
        descriptor.Description("Additional search information.");

        descriptor.Field(m => m.TotalResults).Type<IntType>();
    }
}

public class YelpSearchParametersType : ObjectType<YelpSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<YelpSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for Yelp search.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.FindDesc).Type<StringType>();
        descriptor.Field(p => p.FindLoc).Type<StringType>();
        descriptor.Field(p => p.YelpDomain).Type<StringType>();
        descriptor.Field(p => p.SortBy).Type<StringType>();
        descriptor.Field(p => p.Attrs).Type<StringType>();
        descriptor.Field(p => p.Cflt).Type<StringType>();
        descriptor.Field(p => p.Start).Type<IntType>();
    }
}

public class YelpPlaceSearchParametersType : ObjectType<YelpPlaceSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<YelpPlaceSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for Yelp place requests.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.PlaceId).Type<StringType>();
        descriptor.Field(p => p.YelpDomain).Type<StringType>();
        descriptor.Field(p => p.FullMenu).Type<BooleanType>();
        descriptor.Field(p => p.MenuName).Type<StringType>();
    }
}

public class YelpReviewsSearchParametersType : ObjectType<YelpReviewsSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<YelpReviewsSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for Yelp reviews.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.PlaceId).Type<StringType>();
        descriptor.Field(p => p.YelpDomain).Type<StringType>();
        descriptor.Field(p => p.Language).Type<StringType>();
        descriptor.Field(p => p.SortBy).Type<StringType>();
        descriptor.Field(p => p.Rating).Type<IntType>();
        descriptor.Field(p => p.NotRecommended).Type<BooleanType>();
        descriptor.Field(p => p.Start).Type<IntType>();
        descriptor.Field(p => p.Num).Type<IntType>();
        descriptor.Field(p => p.Q).Type<StringType>();
    }
}

public class YelpSearchInputType : InputObjectType<GetYelpSearchRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetYelpSearchRequest> descriptor)
    {
        descriptor.Name("GetYelpSearchRequest");
        descriptor.Description("Input parameters for Yelp search.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.FindLoc)
            .Type<NonNullType<StringType>>()
            .Description("Location for Yelp search.");

        descriptor.Field(f => f.FindDesc)
            .Type<StringType>()
            .Description("Optional search description.");

        descriptor.Field(f => f.YelpDomain)
            .Type<StringType>()
            .Description("Yelp domain (e.g., www.yelp.com).");

        descriptor.Field(f => f.SortBy)
            .Type<StringType>()
            .Description("Sorting option for results.");

        descriptor.Field(f => f.Attrs)
            .Type<StringType>()
            .Description("Attributes filter.");

        descriptor.Field(f => f.Cflt)
            .Type<StringType>()
            .Description("Category filter.");

        descriptor.Field(f => f.Start)
            .Type<IntType>()
            .Description("Pagination offset.");
    }
}

public class YelpPlaceInputType : InputObjectType<GetYelpPlaceRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetYelpPlaceRequest> descriptor)
    {
        descriptor.Name("GetYelpPlaceRequest");
        descriptor.Description("Input parameters for Yelp place details.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("Yelp place identifier.");

        descriptor.Field(f => f.YelpDomain)
            .Type<StringType>()
            .Description("Yelp domain (e.g., www.yelp.com).");
    }
}

public class YelpPlaceFullMenuInputType : InputObjectType<GetYelpPlaceFullMenuRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetYelpPlaceFullMenuRequest> descriptor)
    {
        descriptor.Name("GetYelpPlaceFullMenuRequest");
        descriptor.Description("Input parameters for Yelp place full menu.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("Yelp place identifier.");

        descriptor.Field(f => f.YelpDomain)
            .Type<StringType>()
            .Description("Yelp domain (e.g., www.yelp.com).");

        descriptor.Field(f => f.FullMenu)
            .Type<BooleanType>()
            .Description("Include full menu data when available.");

        descriptor.Field(f => f.MenuName)
            .Type<StringType>()
            .Description("Optional menu name filter.");
    }
}

public class YelpReviewsInputType : InputObjectType<GetYelpReviewsRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetYelpReviewsRequest> descriptor)
    {
        descriptor.Name("GetYelpReviewsRequest");
        descriptor.Description("Input parameters for Yelp reviews.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("Yelp place identifier.");

        descriptor.Field(f => f.YelpDomain)
            .Type<StringType>()
            .Description("Yelp domain (e.g., www.yelp.com).");

        descriptor.Field(f => f.Language)
            .Type<StringType>()
            .Description("Review language code.");

        descriptor.Field(f => f.SortBy)
            .Type<StringType>()
            .Description("Sorting option for reviews.");

        descriptor.Field(f => f.Rating)
            .Type<IntType>()
            .Description("Filter by rating.");

        descriptor.Field(f => f.NotRecommended)
            .Type<BooleanType>()
            .Description("Include not recommended reviews.");

        descriptor.Field(f => f.Start)
            .Type<IntType>()
            .Description("Pagination offset.");

        descriptor.Field(f => f.Num)
            .Type<IntType>()
            .Description("Number of reviews to return.");

        descriptor.Field(f => f.Q)
            .Type<StringType>()
            .Description("Search term for reviews.");
    }
}

