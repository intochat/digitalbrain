using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.GraphQL.Types;

public class MapsDirectionsType : ObjectType<GetMapsDirectionsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetMapsDirectionsResponse> descriptor)
    {
        descriptor.Name("MapsDirections");
        descriptor.Description("Google Maps directions response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<MapsSearchMetadataType>()
            .Description("Metadata about the directions request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<MapsDirectionsSearchParametersType>()
            .Description("Parameters used for the directions request.");

        descriptor.Field(f => f.PlacesInfo)
            .Type<ListType<AnyType>>()
            .Description("Places information (raw SerpApi objects).");

        descriptor.Field(f => f.Directions)
            .Type<ListType<AnyType>>()
            .Description("Directions steps (raw SerpApi objects).");

        descriptor.Field(f => f.Durations)
            .Type<ListType<AnyType>>()
            .Description("Durations information (raw SerpApi objects).");

        descriptor.Field(f => f.Error)
            .Type<StringType>()
            .Description("Error details from SerpApi.");
    }
}

public class MapsPlaceResultsType : ObjectType<GetMapsPlaceResultsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetMapsPlaceResultsResponse> descriptor)
    {
        descriptor.Name("MapsPlaceResults");
        descriptor.Description("Google Maps place results response from SerpApi.");

        descriptor.Field(f => f.SearchMetadata)
            .Type<MapsSearchMetadataType>()
            .Description("Metadata about the place results request.");

        descriptor.Field(f => f.SearchParameters)
            .Type<MapsPlaceResultsSearchParametersType>()
            .Description("Parameters used for the place results request.");

        descriptor.Field(f => f.PlaceResults)
            .Type<AnyType>()
            .Description("Place results (raw SerpApi object).");

        descriptor.Field(f => f.Error)
            .Type<StringType>()
            .Description("Error details from SerpApi.");
    }
}

public class MapsDirectionsSearchParametersType : ObjectType<MapsDirectionsSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<MapsDirectionsSearchParameters> descriptor)
    {
        descriptor.Name("MapsDirectionsSearchParameters");
        descriptor.Description("Search parameters used for Google Maps directions.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.StartAddr).Type<StringType>();
        descriptor.Field(p => p.EndAddr).Type<StringType>();
        descriptor.Field(p => p.StartDataId).Type<StringType>();
        descriptor.Field(p => p.EndDataId).Type<StringType>();
        descriptor.Field(p => p.StartCoords).Type<StringType>();
        descriptor.Field(p => p.EndCoords).Type<StringType>();
        descriptor.Field(p => p.TravelMode).Type<IntType>();
        descriptor.Field(p => p.DistanceUnit).Type<IntType>();
        descriptor.Field(p => p.Avoid).Type<StringType>();
        descriptor.Field(p => p.Prefer).Type<StringType>();
        descriptor.Field(p => p.Route).Type<IntType>();
        descriptor.Field(p => p.Time).Type<StringType>();
        descriptor.Field(p => p.Hl).Type<StringType>();
        descriptor.Field(p => p.Gl).Type<StringType>();
    }
}

public class MapsPlaceResultsSearchParametersType : ObjectType<MapsPlaceResultsSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<MapsPlaceResultsSearchParameters> descriptor)
    {
        descriptor.Name("MapsPlaceResultsSearchParameters");
        descriptor.Description("Search parameters used for Google Maps place results.");

        descriptor.Field(p => p.Engine).Type<StringType>();
        descriptor.Field(p => p.Type).Type<StringType>();
        descriptor.Field(p => p.Data).Type<StringType>();
        descriptor.Field(p => p.PlaceId).Type<StringType>();
        descriptor.Field(p => p.DataCid).Type<StringType>();
        descriptor.Field(p => p.Gl).Type<StringType>();
    }
}

public class MapsDirectionsInputType : InputObjectType<GetMapsDirectionsRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetMapsDirectionsRequest> descriptor)
    {
        descriptor.Name("GetMapsDirectionsRequest");
        descriptor.Description("Input parameters for Google Maps directions.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.StartAddr)
            .Type<StringType>()
            .Description("Starting address.");

        descriptor.Field(f => f.StartDataId)
            .Type<StringType>()
            .Description("Starting place data id.");

        descriptor.Field(f => f.StartCoords)
            .Type<StringType>()
            .Description("Starting coordinates (lat,lng).");

        descriptor.Field(f => f.EndAddr)
            .Type<StringType>()
            .Description("Ending address.");

        descriptor.Field(f => f.EndDataId)
            .Type<StringType>()
            .Description("Ending place data id.");

        descriptor.Field(f => f.EndCoords)
            .Type<StringType>()
            .Description("Ending coordinates (lat,lng).");

        descriptor.Field(f => f.TravelMode)
            .Type<IntType>()
            .Description("Travel mode (6=best/default, 0=driving, 9=two_wheeler, 3=transit, 2=walking, 1=cycling, 4=flight).");

        descriptor.Field(f => f.DistanceUnit)
            .Type<IntType>()
            .Description("Distance unit (0=metric, 1=imperial).");

        descriptor.Field(f => f.Avoid)
            .Type<StringType>()
            .Description("Comma-separated avoid options (e.g., tolls, highways, ferries).");

        descriptor.Field(f => f.Prefer)
            .Type<StringType>()
            .Description("Preferred options for directions.");

        descriptor.Field(f => f.Route)
            .Type<IntType>()
            .Description("Transit route type (2=less_walking, 3=fewer_transfers, 4=lower_fare).");

        descriptor.Field(f => f.Time)
            .Type<StringType>()
            .Description("Time format: depart_at:<timestamp>, arrive_by:<timestamp>, or last_available.");

        descriptor.Field(f => f.Hl)
            .Type<StringType>()
            .Description("Language code.");

        descriptor.Field(f => f.Gl)
            .Type<StringType>()
            .Description("Country code.");

        descriptor.Field(f => f.NoCache)
            .Type<BooleanType>()
            .Description("Disable cache.");

        descriptor.Field(f => f.Async)
            .Type<BooleanType>()
            .Description("Run request asynchronously.");

        descriptor.Field(f => f.ZeroTrace)
            .Type<BooleanType>()
            .Description("Do not store data in SerpApi logs.");

        descriptor.Field(f => f.Output)
            .Type<StringType>()
            .Description("Output format.");

        descriptor.Field(f => f.JsonRestrictor)
            .Type<StringType>()
            .Description("Restrict JSON response fields.");
    }
}

public class MapsPlaceResultsInputType : InputObjectType<GetMapsPlaceResultsRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetMapsPlaceResultsRequest> descriptor)
    {
        descriptor.Name("GetMapsPlaceResultsRequest");
        descriptor.Description("Input parameters for Google Maps place results.");

        descriptor.Field(f => f.TripVaultName)
            .Type<StringType>()
            .Description("Optional TripVault name. If omitted, the standard vault is used.");

        descriptor.Field(f => f.PlaceId)
            .Type<StringType>()
            .Description("Google Place ID.");

        descriptor.Field(f => f.DataCid)
            .Type<StringType>()
            .Description("Google Maps data CID.");

        descriptor.Field(f => f.Type)
            .Type<StringType>()
            .Description("Request type (place).");

        descriptor.Field(f => f.Data)
            .Type<StringType>()
            .Description("Data parameter for place results.");

        descriptor.Field(f => f.Gl)
            .Type<StringType>()
            .Description("Country code.");

        descriptor.Field(f => f.NoCache)
            .Type<BooleanType>()
            .Description("Disable cache.");

        descriptor.Field(f => f.Async)
            .Type<BooleanType>()
            .Description("Run request asynchronously.");

        descriptor.Field(f => f.ZeroTrace)
            .Type<BooleanType>()
            .Description("Do not store data in SerpApi logs.");

        descriptor.Field(f => f.Output)
            .Type<StringType>()
            .Description("Output format.");

        descriptor.Field(f => f.JsonRestrictor)
            .Type<StringType>()
            .Description("Restrict JSON response fields.");
    }
}

