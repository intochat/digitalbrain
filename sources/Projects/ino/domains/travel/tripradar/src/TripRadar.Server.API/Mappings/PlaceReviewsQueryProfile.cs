using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using GpsCoordinates = TripRadar.Server.API.Contracts.Models.GpsCoordinates;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using PlaceReviewsPagination = TripRadar.Server.Application.DTO.Models.PlaceReviewsPagination;
using SearchMetadata = TripRadar.Server.Application.DTO.Models.SearchMetadata;

namespace TripRadar.Server.API.Mappings;

internal sealed class PlaceReviewsQueryProfile : Profile
{
    public PlaceReviewsQueryProfile()
    {
        CreateMap<GetPlaceReviewsRequest, GetPlaceReviewsRequestDTO>();
        CreateMap<PlaceReviewsFilters, PlaceReviewsFiltersDTO>();
        CreateMap<Contracts.Models.PlaceReviewsPagination, PlaceReviewsPagination>();
        CreateMap<Localization, Application.DTO.Models.Localization>();

        CreateMap<GetPlaceReviewsResponseDTO, GetPlaceReviewsResponse>();
        CreateMap<PlaceReviewsSearchParametersDTO, PlaceReviewsSearchParameters>();
        CreateMap<PlaceReviewsPlaceInfoDTO, PlaceReviewsPlaceInfo>();
        CreateMap<PlaceReviewsTopicDTO, PlaceReviewsTopic>();
        CreateMap<PlaceReviewDTO, PlaceReview>();
        CreateMap<PlaceReviewsOperatingHoursDTO, PlaceReviewsOperatingHours>();
        CreateMap<PlaceReviewsEditorialSummaryDTO, PlaceReviewsEditorialSummary>();
        CreateMap<PlaceReviewsUserReviewDTO, PlaceReviewsUserReview>();
        CreateMap<PlaceReviewsUserDTO, PlaceReviewsUser>();
        CreateMap<PlaceReviewsExtractedSnippetDTO, PlaceReviewsExtractedSnippet>();
        CreateMap<PlaceReviewsDetailsDTO, PlaceReviewsDetails>();
        CreateMap<PlaceReviewsOwnerResponseDTO, PlaceReviewsOwnerResponse>();
        CreateMap<PlaceReviewsPaginationDTO, PlaceReviewsPaginationResult>();
        CreateMap<PlaceReviewsSerpApiPaginationDTO, PlaceReviewsSerpApiPagination>();
        CreateMap<GpsCoordinatesDTO, GpsCoordinates>();
        CreateMap<SearchMetadata, Contracts.Models.SearchMetadata>();
    }
}
