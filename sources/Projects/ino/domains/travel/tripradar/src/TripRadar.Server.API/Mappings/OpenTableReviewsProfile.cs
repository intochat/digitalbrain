using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class OpenTableReviewsProfile : Profile
{
    public OpenTableReviewsProfile()
    {
        CreateMap<GetOpenTableReviewsRequest, GetOpenTableReviewsRequestDTO>();

        CreateMap<OpenTableSearchParametersDTO, OpenTableSearchParameters>();
        CreateMap<OpenTableSearchInformationDTO, OpenTableSearchInformation>();
        CreateMap<OpenTableReviewsSummaryDTO, OpenTableReviewsSummary>();
        CreateMap<OpenTableRatingsSummaryDTO, OpenTableRatingsSummary>();
        CreateMap<OpenTableRatingBreakdownDTO, OpenTableRatingBreakdown>();
        CreateMap<OpenTableAwardDTO, OpenTableAward>();
        CreateMap<OpenTableReviewDTO, OpenTableReview>();
        CreateMap<OpenTableReviewRatingsDTO, OpenTableReviewRatings>();
        CreateMap<OpenTableReviewUserDTO, OpenTableReviewUser>();
        CreateMap<OpenTableReviewHelpfulnessDTO, OpenTableReviewHelpfulness>();
        CreateMap<OpenTableReviewImageDTO, OpenTableReviewImage>();
        CreateMap<OpenTableReviewImageVariantDTO, OpenTableReviewImageVariant>();
        CreateMap<OpenTableReviewResponseDTO, OpenTableReviewResponse>();
        CreateMap<OpenTableSerpApiPaginationDTO, OpenTableSerpApiPagination>();

        CreateMap<GetOpenTableReviewsResponseDTO, GetOpenTableReviewsResponse>();
    }
}
