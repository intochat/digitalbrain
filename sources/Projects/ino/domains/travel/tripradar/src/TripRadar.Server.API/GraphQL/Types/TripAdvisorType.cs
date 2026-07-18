using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class TripAdvisorSearchType : ObjectType<GetTripAdvisorSearchResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetTripAdvisorSearchResponse> descriptor)
    {
        descriptor.Description("Represents a TripAdvisor search response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<TripAdvisorSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<TripAdvisorSearchParametersType>()
            .Description("Parameters used for the TripAdvisor search.");

        descriptor.Field(f => f.SearchInformation)
            .Type<TripAdvisorSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.Places)
            .Type<ListType<AnyType>>()
            .Description("TripAdvisor places results (raw SerpApi objects).");

        descriptor.Field(f => f.Forums)
            .Type<ListType<AnyType>>()
            .Description("TripAdvisor forum results (raw SerpApi objects).");

        descriptor.Field(f => f.Locations)
            .Type<ListType<AnyType>>()
            .Description("TripAdvisor location results (raw SerpApi objects).");

        descriptor.Field(f => f.SerpapiPagination)
            .Type<AnyType>()
            .Description("SerpApi pagination details.");
    }
}

public class TripAdvisorPlaceType : ObjectType<GetTripAdvisorPlaceResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetTripAdvisorPlaceResponse> descriptor)
    {
        descriptor.Description("Represents a TripAdvisor place detail response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<TripAdvisorPlaceSearchMetadataType>()
            .Description("Metadata about the search request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<TripAdvisorPlaceSearchParametersType>()
            .Description("Parameters used for the TripAdvisor place search.");

        descriptor.Field(f => f.SearchInformation)
            .Type<TripAdvisorSearchInformationType>()
            .Description("Additional search information.");

        descriptor.Field(f => f.PlaceResult)
            .Type<AnyType>()
            .Description("TripAdvisor place result (raw SerpApi object).");
    }
}

public class TripAdvisorSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("TripAdvisorSearchMetadata");
        descriptor.Description("Metadata information about the TripAdvisor search request.");

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

public class TripAdvisorPlaceSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Name("TripAdvisorPlaceSearchMetadata");
        descriptor.Description("Metadata information about the TripAdvisor place request.");

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

public class TripAdvisorSearchInformationType : ObjectType<SearchInformation>
{
    protected override void Configure(IObjectTypeDescriptor<SearchInformation> descriptor)
    {
        descriptor.Name("TripAdvisorSearchInformation");
        descriptor.Description("Additional search information.");

        descriptor.Field(m => m.TotalResults).Type<IntType>();
    }
}

public class TripAdvisorSearchParametersType : ObjectType<TripAdvisorSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<TripAdvisorSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for TripAdvisor search.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.Q).Type<StringType>();
        descriptor.Field(p => p.TripadvisorDomain).Type<StringType>();
        descriptor.Field(p => p.Ssrc).Type<StringType>();
        descriptor.Field(p => p.Offset).Type<IntType>();
        descriptor.Field(p => p.Limit).Type<IntType>();
        descriptor.Field(p => p.Lat).Type<FloatType>();
        descriptor.Field(p => p.Lon).Type<FloatType>();
    }
}

public class TripAdvisorPlaceSearchParametersType : ObjectType<TripAdvisorPlaceSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<TripAdvisorPlaceSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for TripAdvisor place search.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.PlaceId).Type<StringType>();
        descriptor.Field(p => p.TripadvisorDomain).Type<StringType>();
    }
}

public class TripAdvisorSearchInputType : InputObjectType<GetTripAdvisorSearchRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetTripAdvisorSearchRequest> descriptor)
    {
        descriptor.Name("GetTripAdvisorSearchRequest");
        descriptor.Description("Input parameters for TripAdvisor search.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.Q)
            .Type<NonNullType<StringType>>()
            .Description("Search query.");

        descriptor.Field(f => f.Lat)
            .Type<FloatType>()
            .Description("Latitude for geographic search.");

        descriptor.Field(f => f.Lon)
            .Type<FloatType>()
            .Description("Longitude for geographic search.");

        descriptor.Field(f => f.TripadvisorDomain)
            .Type<StringType>()
            .Description("TripAdvisor domain (e.g., tripadvisor.com).");

        descriptor.Field(f => f.Ssrc)
            .Type<StringType>()
            .Description("Search filter code for TripAdvisor.");

        descriptor.Field(f => f.Offset)
            .Type<IntType>()
            .Description("Offset for pagination.");

        descriptor.Field(f => f.Limit)
            .Type<IntType>()
            .Description("Limit for pagination.");
    }
}

public class TripAdvisorPlaceInputType : InputObjectType<GetTripAdvisorPlaceRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetTripAdvisorPlaceRequest> descriptor)
    {
        descriptor.Name("GetTripAdvisorPlaceRequest");
        descriptor.Description("Input parameters for TripAdvisor place details.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.PlaceId)
            .Type<NonNullType<StringType>>()
            .Description("TripAdvisor place identifier.");

        descriptor.Field(f => f.TripadvisorDomain)
            .Type<StringType>()
            .Description("TripAdvisor domain (e.g., tripadvisor.com).");
    }
}

