using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class TripAdvisorQueryProfile : Profile
{
    public TripAdvisorQueryProfile()
    {
        CreateMap<GetTripAdvisorSearchRequest, GetTripAdvisorSearchRequestDTO>();
        CreateMap<GetTripAdvisorPlaceRequest, GetTripAdvisorPlaceRequestDTO>();

        CreateMap<TripAdvisorSearchParametersDTO, TripAdvisorSearchParameters>();
        CreateMap<TripAdvisorPlaceSearchParametersDTO, TripAdvisorPlaceSearchParameters>();

        CreateMap<GetTripAdvisorSearchResponseDTO, GetTripAdvisorSearchResponse>();
        CreateMap<GetTripAdvisorPlaceResponseDTO, GetTripAdvisorPlaceResponse>();
    }
}
