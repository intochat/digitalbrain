using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.SearchEngine.Maps.Queries.GetMaps;
using SearchQuery = TripRadar.Server.Application.DTO.Models.SearchQuery;

namespace TripRadar.Server.API.Mappings;

internal sealed class MapsQueryProfile : Profile
{
    public MapsQueryProfile()
    {
        CreateMap<GetMapsRequest, GetMapsRequestDTO>()
            .ForMember(dest => dest.PlaceId, opt => opt.MapFrom(src => src.PlaceId))
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => src.Data))
            .ForMember(dest => dest.SearchQuery, opt => opt.MapFrom(src => src.SearchQuery))
            .ForMember(dest => dest.Ll, opt => opt.MapFrom(src => src.Ll))
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => src.Type))
            .ForMember(dest => dest.Localization, opt => opt.MapFrom(src => src.Localization))
            .ForMember(dest => dest.Pagination, opt => opt.MapFrom(src => src.Pagination))
            .ForMember(dest => dest.NoCache, opt => opt.MapFrom(src => src.NoCache));

        CreateMap<GetMapsRequest, GetMapsQuery>()
            .ForMember(dest => dest.Request, opt => opt.MapFrom(src => src))
            .ForMember(dest => dest.Username, opt => opt.Ignore());

        CreateMap<MapsPagination, MapsPaginationDTO>();
        CreateMap<Contracts.Models.SearchQuery, SearchQuery>();
        CreateMap<Contracts.Models.Localization, Application.DTO.Models.Localization>()
            .ForMember(dest => dest.Domain, opt => opt.MapFrom(src => src.GoogleDomain));

        CreateMap<GetMapsResponseDTO, GetMapsResponse>()
            .ForMember(dest => dest.SearchMetadata, opt => opt.MapFrom(src => src.SearchMetadata))
            .ForMember(dest => dest.SearchParameters, opt => opt.MapFrom(src => src.SearchParameters))
            .ForMember(dest => dest.LocalResults, opt => opt.MapFrom(src => src.LocalResults))
            .ForMember(dest => dest.PlaceResults, opt => opt.MapFrom(src => src.PlaceResults));

        CreateMap<MapsSearchParametersDTO, MapsSearchParameters>();
        CreateMap<MapsPlaceResultDTO, MapsPlaceResult>();
        CreateMap<MapsMenuDTO, MapsMenu>();
        CreateMap<MapsExtensionDTO, MapsExtension>();
        CreateMap<MapsImageDTO, MapsImage>();
        CreateMap<MapsUserReviewsDTO, MapsUserReviews>();
        CreateMap<MapsReviewSummaryDTO, MapsReviewSummary>();
        CreateMap<MapsReviewDTO, MapsReview>();
        CreateMap<MapsRelatedSearchDTO, MapsRelatedSearch>();
        CreateMap<MapsPopularTimesDTO, MapsPopularTimes>();
        CreateMap<MapsPopularTimeSlotDTO, MapsPopularTimeSlot>();
        CreateMap<MapsLiveHashDTO, MapsLiveHash>();
        CreateMap<MapsEventDTO, MapsEvent>();
        CreateMap<MapsEventDateDTO, MapsEventDate>();
        CreateMap<MapsTicketInfoDTO, MapsTicketInfo>();
        CreateMap<MapsQADTO, MapsQA>();
        CreateMap<MapsQuestionDTO, MapsQuestion>();
        CreateMap<MapsAnswerDTO, MapsAnswer>();
        CreateMap<MapsUserDTO, MapsUser>();
        CreateMap<MapsAtThisPlaceDTO, MapsAtThisPlace>();
        CreateMap<MapsPlaceTypeDTO, MapsPlaceType>();
        CreateMap<MapsSubPlaceDTO, MapsSubPlace>();
        CreateMap<MapsAdmissionDTO, MapsAdmission>();
        CreateMap<MapsAdmissionOptionDTO, MapsAdmissionOption>();
        CreateMap<MapsExperienceDTO, MapsExperience>();
        CreateMap<MapsPostDTO, MapsPost>();
        CreateMap<MapsWeatherDTO, MapsWeather>();
        CreateMap<MapsAtLocationDTO, MapsAtLocation>();
    }
}
