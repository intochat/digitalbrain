using AutoMapper;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class UsageProfile : Profile
{
    public UsageProfile()
    {
        CreateMap<GetUsageEventsResponseDTO, GetUsageEventsResponse>();
        CreateMap<UsageEventsSummaryDTO, UsageSummaryResponse>();
        CreateMap<UsageTimelinePointDTO, UsageTimelinePointResponse>();
        CreateMap<UsageEventItemDTO, UsageEventItemResponse>();
        CreateMap<UsageTripVaultDTO, UsageTripVaultResponse>();
        CreateMap<UsagePaginationDTO, UsagePaginationResponse>();
    }
}
