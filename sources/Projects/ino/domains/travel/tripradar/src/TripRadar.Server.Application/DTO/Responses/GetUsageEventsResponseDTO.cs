namespace TripRadar.Server.Application.DTO.Responses;

public sealed record GetUsageEventsResponseDTO(
    UsageEventsSummaryDTO Summary,
    IReadOnlyList<UsageTimelinePointDTO> Timeline,
    IReadOnlyList<UsageEventItemDTO> Events,
    UsagePaginationDTO Pagination);

public sealed record UsageEventsSummaryDTO(
    decimal CurrentUsage,
    decimal MonthlyLimit,
    decimal RemainingTokens);

public sealed record UsageTimelinePointDTO(
    DateOnly Date,
    decimal TokensConsumed,
    int EventsCount);

public sealed record UsageTripVaultDTO(
    Guid UniqueId,
    string Name);

public sealed record UsageEventItemDTO(
    Guid UniqueId,
    DateTime OccurredAt,
    string ServiceType,
    string Source,
    decimal TokensConsumed,
    UsageTripVaultDTO? TripVault);

public sealed record UsagePaginationDTO(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
