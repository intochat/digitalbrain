using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class YouTubeSearchType : ObjectType<GetYouTubeSearchResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetYouTubeSearchResponse> descriptor)
    {
        descriptor.Name("YouTubeSearch");
        descriptor.Description("Represents a YouTube search response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<YouTubeSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<YouTubeSearchParametersType>()
            .Description("Parameters used for the YouTube search.");

        descriptor.Field(f => f.SearchInformation)
            .Type<YouTubeSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.VideoResults)
            .Type<ListType<AnyType>>()
            .Description("Video results (raw SerpApi objects).");

        descriptor.Field(f => f.PlaylistResults)
            .Type<ListType<AnyType>>()
            .Description("Playlist results (raw SerpApi objects).");

        descriptor.Field(f => f.ChannelResults)
            .Type<ListType<AnyType>>()
            .Description("Channel results (raw SerpApi objects).");

        descriptor.Field(f => f.MovieResults)
            .Type<ListType<AnyType>>()
            .Description("Movie results (raw SerpApi objects).");

        descriptor.Field(f => f.AdsResults)
            .Type<ListType<AnyType>>()
            .Description("Ads results (raw SerpApi objects).");

        descriptor.Field(f => f.SerpapiPagination)
            .Type<AnyType>()
            .Description("SerpApi pagination details.");

        descriptor.Field(f => f.Pagination)
            .Type<AnyType>()
            .Description("Pagination details.");
    }
}

public class YouTubeSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("YouTubeSearchMetadata");
        descriptor.Description("Metadata information about the YouTube search request.");

        descriptor.Field(m => m.Id).Type<StringType>();
        descriptor.Field(m => m.Status).Type<StringType>();
        descriptor.Field(m => m.JsonEndpoint).Type<StringType>();
        descriptor.Field(m => m.CreatedAt).Type<StringType>();
        descriptor.Field(m => m.ProcessedAt).Type<StringType>();
        descriptor.Field(m => m.RawHtmlFile).Type<StringType>();
        descriptor.Field(m => m.PrettifyHtmlFile).Type<StringType>();
        descriptor.Field(m => m.TotalTimeTaken).Type<FloatType>();
    }
}

public class YouTubeSearchParametersType : ObjectType<YouTubeSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<YouTubeSearchParameters> descriptor)
    {
        descriptor.Name("YouTubeSearchParameters");
        descriptor.Description("Parameters used for YouTube search.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.SearchQuery).Type<StringType>();
        descriptor.Field(p => p.Gl).Type<StringType>();
        descriptor.Field(p => p.Hl).Type<StringType>();
        descriptor.Field(p => p.Sp).Type<StringType>();
        descriptor.Field(p => p.NoCache).Type<BooleanType>();
        descriptor.Field(p => p.Async).Type<BooleanType>();
        descriptor.Field(p => p.ZeroTrace).Type<BooleanType>();
        descriptor.Field(p => p.Output).Type<StringType>();
        descriptor.Field(p => p.JsonRestrictor).Type<StringType>();
    }
}

public class YouTubeSearchInformationType : ObjectType<YouTubeSearchInformation>
{
    protected override void Configure(IObjectTypeDescriptor<YouTubeSearchInformation> descriptor)
    {
        descriptor.Name("YouTubeSearchInformation");
        descriptor.Description("Additional YouTube search information.");

        descriptor.Field(i => i.TotalResults).Type<IntType>();
        descriptor.Field(i => i.VideoResultsState).Type<StringType>();
    }
}

public class YouTubeSearchInputType : InputObjectType<GetYouTubeSearchRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetYouTubeSearchRequest> descriptor)
    {
        descriptor.Name("GetYouTubeSearchRequest");
        descriptor.Description("Input parameters for YouTube search.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.SearchQuery)
            .Name("search_query")
            .Type<NonNullType<StringType>>()
            .Description("Search query for YouTube.");

        descriptor.Field(f => f.Gl)
            .Name("gl")
            .Type<StringType>()
            .Description("Country code for localization (e.g., 'us').");

        descriptor.Field(f => f.Hl)
            .Name("hl")
            .Type<StringType>()
            .Description("Language code for localization (e.g., 'en').");

        descriptor.Field(f => f.Sp)
            .Name("sp")
            .Type<StringType>()
            .Description("Pagination or filter token.");

        descriptor.Field(f => f.NoCache)
            .Name("no_cache")
            .Type<BooleanType>()
            .Description("Whether to bypass cache.");

        descriptor.Field(f => f.Async)
            .Name("async")
            .Type<BooleanType>()
            .Description("Whether to run the request asynchronously.");

        descriptor.Field(f => f.ZeroTrace)
            .Name("zero_trace")
            .Type<BooleanType>()
            .Description("Whether to enable zero-trace (enterprise only).");

        descriptor.Field(f => f.Output)
            .Name("output")
            .Type<StringType>()
            .Description("Output format: json or html.");

        descriptor.Field(f => f.JsonRestrictor)
            .Name("json_restrictor")
            .Type<StringType>()
            .Description("JSON restrictor to filter response fields.");
    }
}

