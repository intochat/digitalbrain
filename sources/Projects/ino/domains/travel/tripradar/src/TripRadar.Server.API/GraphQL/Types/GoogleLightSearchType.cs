using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class GoogleLightSearchType : ObjectType<GetGoogleLightSearchResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetGoogleLightSearchResponse> descriptor)
    {
        descriptor.Name("GoogleLightSearch");
        descriptor.Description("Represents a Google Light search response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<GoogleLightSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<GoogleLightSearchParametersType>()
            .Description("Parameters used for the Google Light search.");

        descriptor.Field(f => f.SearchInformation)
            .Type<GoogleLightSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.OrganicResults)
            .Type<ListType<AnyType>>()
            .Description("Organic search results (raw SerpApi objects).");

        descriptor.Field(f => f.RelatedSearches)
            .Type<ListType<AnyType>>()
            .Description("Related searches (raw SerpApi objects).");

        descriptor.Field(f => f.Pagination)
            .Type<AnyType>()
            .Description("Pagination details.");

        descriptor.Field(f => f.SerpApiPagination)
            .Name("serpapiPagination")
            .Type<AnyType>()
            .Description("SerpApi pagination details.");

        descriptor.Field(f => f.AdditionalProperties)
            .Name("raw")
            .Type<AnyType>()
            .Description("Additional fields returned by SerpApi.");
    }
}

public class GoogleLightSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("GoogleLightSearchMetadata");
        descriptor.Description("Metadata information about the Google Light search request.");

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

public class GoogleLightSearchParametersType : ObjectType<GoogleLightSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<GoogleLightSearchParameters> descriptor)
    {
        descriptor.Name("GoogleLightSearchParameters");
        descriptor.Description("Parameters used for Google Light search.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.Q).Type<StringType>();
        descriptor.Field(p => p.Location).Type<StringType>();
        descriptor.Field(p => p.Uule).Type<StringType>();
        descriptor.Field(p => p.GoogleDomain).Type<StringType>();
        descriptor.Field(p => p.Gl).Type<StringType>();
        descriptor.Field(p => p.Hl).Type<StringType>();
        descriptor.Field(p => p.Lr).Type<StringType>();
        descriptor.Field(p => p.AsDt).Type<StringType>();
        descriptor.Field(p => p.AsEpq).Type<StringType>();
        descriptor.Field(p => p.AsEq).Type<StringType>();
        descriptor.Field(p => p.AsLq).Type<StringType>();
        descriptor.Field(p => p.AsNlo).Type<StringType>();
        descriptor.Field(p => p.AsNhi).Type<StringType>();
        descriptor.Field(p => p.AsOq).Type<StringType>();
        descriptor.Field(p => p.AsQ).Type<StringType>();
        descriptor.Field(p => p.AsQdr).Type<StringType>();
        descriptor.Field(p => p.AsRq).Type<StringType>();
        descriptor.Field(p => p.AsSitesearch).Type<StringType>();
        descriptor.Field(p => p.Safe).Type<StringType>();
        descriptor.Field(p => p.Nfpr).Type<BooleanType>();
        descriptor.Field(p => p.Filter).Type<BooleanType>();
        descriptor.Field(p => p.Start).Type<IntType>();
        descriptor.Field(p => p.Device).Type<StringType>();
        descriptor.Field(p => p.NoCache).Type<BooleanType>();
        descriptor.Field(p => p.Async).Type<BooleanType>();
        descriptor.Field(p => p.ZeroTrace).Type<BooleanType>();
        descriptor.Field(p => p.Output).Type<StringType>();
        descriptor.Field(p => p.JsonRestrictor).Type<StringType>();
    }
}

public class GoogleLightSearchInformationType : ObjectType<SearchInformation>
{
    protected override void Configure(IObjectTypeDescriptor<SearchInformation> descriptor)
    {
        descriptor.Name("GoogleLightSearchInformation");
        descriptor.Description("Additional Google Light search information.");

        descriptor.Field(i => i.TotalResults).Type<IntType>();
    }
}

public class GoogleLightSearchInputType : InputObjectType<GetGoogleLightSearchRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetGoogleLightSearchRequest> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Name("GetGoogleLightSearchRequest");
        descriptor.Description("Input parameters for Google Light search.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.Q)
            .Name("q")
            .Type<StringType>()
            .Description("Legacy search query. Prefer searchQuery.q.")
            .Deprecated("Use searchQuery.q");

        descriptor.Field(f => f.SearchQuery)
            .Type<GoogleLightSearchQueryInputType>()
            .Description("Search query parameters.");

        descriptor.Field(f => f.GeographicLocation)
            .Type<GoogleLightGeographicLocationInputType>()
            .Description("Geographic location parameters for the search.");

        descriptor.Field(f => f.Localization)
            .Type<GoogleLightLocalizationInputType>()
            .Description("Localization options such as language, country, and domain.");

        descriptor.Field(f => f.Pagination)
            .Type<GoogleLightPaginationInputType>()
            .Description("Pagination options for the search results.");

        descriptor.Field(f => f.Lr)
            .Name("lr")
            .Type<StringType>()
            .Description("Language restrictor (e.g., lang_en|lang_fr).");

        descriptor.Field(f => f.AsDt)
            .Name("as_dt")
            .Type<StringType>()
            .Description("Include or exclude results from a site.");

        descriptor.Field(f => f.AsEpq)
            .Name("as_epq")
            .Type<StringType>()
            .Description("Exact phrase to include.");

        descriptor.Field(f => f.AsEq)
            .Name("as_eq")
            .Type<StringType>()
            .Description("Word or phrase to exclude.");

        descriptor.Field(f => f.AsLq)
            .Name("as_lq")
            .Type<StringType>()
            .Description("Required link to a URL.");

        descriptor.Field(f => f.AsNlo)
            .Name("as_nlo")
            .Type<StringType>()
            .Description("Start value for a numeric range.");

        descriptor.Field(f => f.AsNhi)
            .Name("as_nhi")
            .Type<StringType>()
            .Description("End value for a numeric range.");

        descriptor.Field(f => f.AsOq)
            .Name("as_oq")
            .Type<StringType>()
            .Description("Additional optional terms.");

        descriptor.Field(f => f.AsQ)
            .Name("as_q")
            .Type<StringType>()
            .Description("Additional required terms.");

        descriptor.Field(f => f.AsQdr)
            .Name("as_qdr")
            .Type<StringType>()
            .Description("Quick date range filter.");

        descriptor.Field(f => f.AsRq)
            .Name("as_rq")
            .Type<StringType>()
            .Description("Related results to a URL.");

        descriptor.Field(f => f.AsSitesearch)
            .Name("as_sitesearch")
            .Type<StringType>()
            .Description("Restrict results to a site.");

        descriptor.Field(f => f.Safe)
            .Name("safe")
            .Type<StringType>()
            .Description("SafeSearch level: active or off.");

        descriptor.Field(f => f.Nfpr)
            .Name("nfpr")
            .Type<BooleanType>()
            .Description("Exclude auto-corrected results.");

        descriptor.Field(f => f.Filter)
            .Name("filter")
            .Type<BooleanType>()
            .Description("Enable or disable similar/omitted results filtering.");

        descriptor.Field(f => f.Device)
            .Name("device")
            .Type<StringType>()
            .Description("Device type: desktop, tablet, or mobile.");

        descriptor.Field(f => f.NoCache)
            .Name("no_cache")
            .Type<BooleanType>()
            .Description("Bypass cache.");

        descriptor.Field(f => f.Async)
            .Name("async")
            .Type<BooleanType>()
            .Description("Submit the search asynchronously.");

        descriptor.Field(f => f.ZeroTrace)
            .Name("zero_trace")
            .Type<BooleanType>()
            .Description("Enable ZeroTrace mode (enterprise only).");

        descriptor.Field(f => f.Output)
            .Name("output")
            .Type<StringType>()
            .Description("Output format: json or html.");

        descriptor.Field(f => f.JsonRestrictor)
            .Name("json_restrictor")
            .Type<StringType>()
            .Description("JSON restrictor for smaller responses.");
    }
}

public class GoogleLightSearchQueryInputType : InputObjectType<SearchQuery>
{
    protected override void Configure(IInputObjectTypeDescriptor<SearchQuery> descriptor)
    {
        descriptor.Name("GoogleLightSearchQuery");
        descriptor.Description("Search query parameters for Google Light.");

        descriptor.Field(f => f.Q)
            .Type<NonNullType<StringType>>()
            .Description("Search query.");
    }
}

public class GoogleLightGeographicLocationInputType : InputObjectType<GeographicLocation>
{
    protected override void Configure(IInputObjectTypeDescriptor<GeographicLocation> descriptor)
    {
        descriptor.Name("GoogleLightGeographicLocation");
        descriptor.Description("Geographic location parameters for Google Light.");

        descriptor.Field(f => f.Location)
            .Type<StringType>()
            .Description("Location for the search origin.");

        descriptor.Field(f => f.Uule)
            .Type<StringType>()
            .Description("Google encoded location for the search origin.");
    }
}

public class GoogleLightLocalizationInputType : InputObjectType<Localization>
{
    protected override void Configure(IInputObjectTypeDescriptor<Localization> descriptor)
    {
        descriptor.Name("GoogleLightLocalization");
        descriptor.Description("Localization settings for Google Light.");

        descriptor.Field(f => f.Hl)
            .Type<StringType>()
            .Description("Language code for localization (e.g., 'en').");

        descriptor.Field(f => f.Gl)
            .Type<StringType>()
            .Description("Country code for localization (e.g., 'us').");

        descriptor.Field(f => f.Currency)
            .Type<StringType>()
            .Description("Currency code (e.g., 'USD').");

        descriptor.Field(f => f.GoogleDomain)
            .Type<StringType>()
            .Description("Google domain to use (e.g., google.com).");
    }
}

public class GoogleLightPaginationInputType : InputObjectType<Pagination>
{
    protected override void Configure(IInputObjectTypeDescriptor<Pagination> descriptor)
    {
        descriptor.Name("GoogleLightPagination");
        descriptor.Description("Pagination options for Google Light.");

        descriptor.Field(f => f.Start)
            .Type<IntType>()
            .Description("Result offset for pagination.");
    }
}

