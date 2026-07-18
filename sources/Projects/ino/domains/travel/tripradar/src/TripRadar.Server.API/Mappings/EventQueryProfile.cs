using System.Globalization;
using System.Text.Json;
using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.SearchEngine.Events.Commands.CreateScheduledEventQuery;
using EventFilters = TripRadar.Server.API.Contracts.Models.EventFilters;
using EventSearchInformation = TripRadar.Server.API.Contracts.Models.EventSearchInformation;
using GeographicLocation = TripRadar.Server.Application.DTO.Models.GeographicLocation;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using Pagination = TripRadar.Server.API.Contracts.Models.Pagination;
using QueryColumn = TripRadar.Server.Domain.ValueObjects.QueryColumn;
using SearchMetadata = TripRadar.Server.API.Contracts.Models.SearchMetadata;
using SearchQuery = TripRadar.Server.Application.DTO.Models.SearchQuery;

namespace TripRadar.Server.API.Mappings;

internal sealed class EventQueryProfile : Profile
{
    public EventQueryProfile()
    {
        CreateMap<GetEventRequest, GetEventRequestDTO>()
            .ForMember(dest => dest.SearchQuery, opt => opt.MapFrom(src => src.Search))
            .ForMember(dest => dest.GeographicLocation, opt => opt.MapFrom(src => src.GeographicLocation))
            .ForMember(dest => dest.Localization, opt => opt.MapFrom(src => src.Localization))
            .ForMember(dest => dest.Filters, opt => opt.MapFrom(src => src.Filters))
            .ForMember(dest => dest.NextPage, opt => opt.MapFrom(src => src.Pagination));

        CreateMap<CreateScheduledEventQueryRequest, CreateScheduledEventQueryCommand>()
            .ConstructUsing(src => new CreateScheduledEventQueryCommand(
                string.Empty,
                src.SearchQuery,
                src.SelectedColumns != null
                    ? src.SelectedColumns.Select(i => new QueryColumn(i.Name, i.IsActive)).ToList()
                    : new List<QueryColumn>(),
                SerializeAdditionalParameters(src),
                src.NextExecutionTime,
                src.Schedule
            ));

        CreateMap<CreateScheduledEventQueryCommand, GetEventRequestDTO>();

        CreateMap<Contracts.Models.SearchQuery, SearchQuery>();
        CreateMap<Contracts.Models.GeographicLocation, GeographicLocation>();
        CreateMap<Localization, Application.DTO.Models.Localization>();
        CreateMap<EventFilters, Application.DTO.Models.EventFilters>();
        CreateMap<TokenPagination, PageToken>();

        CreateMap<Pagination, PageToken>()
            .ForMember(dest => dest.NextPageToken,
                opt => opt.MapFrom(src => src.Start.HasValue ? src.Start.Value.ToString(CultureInfo.InvariantCulture) : null));

        CreateMap<GetEventResponseDTO, GetEventsResponse>();
        CreateMap<Application.DTO.Models.SearchMetadata, SearchMetadata>();
        CreateMap<Application.DTO.Models.EventSearchInformation, EventSearchInformation>();
        CreateMap<EventDTO, Event>();
        CreateMap<EventDateDTO, EventDate>();
        CreateMap<VenueDTO, Venue>();
        CreateMap<TicketInfoDTO, TicketInfo>();
        CreateMap<EventSearchParametersDTO, EventSearchParameters>();
        CreateMap<EventLocationMapDTO, EventLocationMap>();

        CreateMap<Contracts.Models.QueryColumn, QueryColumn>();
    }

    private static string? SerializeAdditionalParameters(CreateScheduledEventQueryRequest request)
    {
        var parameters = request.AdditionalParameters is null
            ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(request.AdditionalParameters, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            parameters["location"] = request.Location.Trim();
        }

        if (request.StartDate.HasValue)
        {
            parameters["startDate"] = NormalizeUtc(request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            parameters["endDate"] = NormalizeUtc(request.EndDate.Value);
        }

        return parameters.Count == 0 ? null : JsonSerializer.Serialize(parameters);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

