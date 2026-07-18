using System.Text.Json;
using AutoMapper;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.SearchEngine.Maps.Queries.GetMaps;

namespace TripRadar.Server.Infrastructure.Mappings;

public class MapsQueryProfile : Profile
{
    public MapsQueryProfile()
    {
        CreateMap<GetMapsQuery, GetMapsRequestDTO>();
        CreateMap<string, GetMapsResponseDTO>()
            .ConvertUsing<JsonStringToMapsResponseConverter>();
    }
}

public class JsonStringToMapsResponseConverter : ITypeConverter<string, GetMapsResponseDTO>
{
    public GetMapsResponseDTO Convert(string source, GetMapsResponseDTO destination, ResolutionContext context)
    {
        if (string.IsNullOrEmpty(source))
        {
            return new GetMapsResponseDTO();
        }

        try
        {
            return JsonSerializer.Deserialize<GetMapsResponseDTO>(source) ?? new GetMapsResponseDTO();
        }
        catch
        {
            return new GetMapsResponseDTO();
        }
    }
}
