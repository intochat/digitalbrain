using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class YouTubeSearchProfile : Profile
{
    public YouTubeSearchProfile()
    {
        CreateMap<GetYouTubeSearchRequest, GetYouTubeSearchRequestDTO>();

        CreateMap<YouTubeSearchParametersDTO, YouTubeSearchParameters>();
        CreateMap<YouTubeSearchInformationDTO, YouTubeSearchInformation>();

        CreateMap<GetYouTubeSearchResponseDTO, GetYouTubeSearchResponse>();
    }
}
