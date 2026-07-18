using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Commands.CreateScheduledLocalPlacesQuery;
using TripRadar.Server.Comms.Core.Extensions;
using GeographicLocation = TripRadar.Server.Application.DTO.Models.GeographicLocation;
using GpsCoordinates = TripRadar.Server.API.Contracts.Models.GpsCoordinates;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using Pagination = TripRadar.Server.Application.DTO.Models.Pagination;
using QueryColumn = TripRadar.Server.Domain.ValueObjects.QueryColumn;
using SearchQuery = TripRadar.Server.Application.DTO.Models.SearchQuery;

namespace TripRadar.Server.API.Mappings;

internal sealed class LocalPlacesQueryProfile : Profile
{
    public LocalPlacesQueryProfile()
    {
        CreateMap<GetLocalPlacesRequest, GetLocalPlacesRequestDTO>();

        CreateMap<CreateScheduledLocalPlacesQueryRequest, CreateScheduledLocalPlacesQueryCommand>()
            .ConstructUsing(src => new CreateScheduledLocalPlacesQueryCommand(
                string.Empty,
                src.SearchQuery,
                src.Location,
                src.Radius,
                src.Schedule,
                src.NextExecutionTime,
                src.AdditionalParameters.SerializeParameters(),
                src.SelectedColumns != null
                    ? src.SelectedColumns.Select(i => new QueryColumn(i.Name, i.IsActive)).ToList()
                    : new List<QueryColumn>()
            ));

        CreateMap<Contracts.Models.SearchQuery, SearchQuery>();
        CreateMap<Contracts.Models.GeographicLocation, GeographicLocation>();
        CreateMap<LocalPlacesFilters, LocalPlacesFiltersDTO>();
        CreateMap<Contracts.Models.Pagination, Pagination>();
        CreateMap<Localization, Application.DTO.Models.Localization>();

        CreateMap<GetLocalPlacesResponseDTO, GetLocalPlacesResponse>();
        CreateMap<LocalMapDTO, LocalMap>();
        CreateMap<LocalSearchParametersDTO, LocalSearchParameters>();
        CreateMap<LocalAdvertisementResultDTO, LocalAdvertisementResult>();
        CreateMap<LocalPlaceResultDTO, LocalPlaceResult>();
        CreateMap<PlaceLinksDTO, PlaceLinks>();
        CreateMap<GpsCoordinatesDTO, GpsCoordinates>();
        CreateMap<ServiceOptionsDTO, ServiceOptions>();
        CreateMap<DiscoverMorePlaceDTO, DiscoverMorePlace>();
        CreateMap<LocalPaginationDTO, LocalPagination>();
        CreateMap<LocalSerpApiPaginationDTO, LocalSerpApiPagination>();
    }
}
