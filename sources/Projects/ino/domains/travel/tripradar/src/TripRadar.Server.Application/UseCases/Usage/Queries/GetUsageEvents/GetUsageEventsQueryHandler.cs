using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Usage.Queries.GetUsageEvents;

public sealed class GetUsageEventsQueryHandler(ICurrentUserContext currentUserContext, ITierLimitService tierLimitService, IUsageEventRepository usageEventRepository) : IRequestHandler<GetUsageEventsQuery, Result<GetUsageEventsResponseDTO>>
{
    public async Task<Result<GetUsageEventsResponseDTO>> Handle(GetUsageEventsQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        var (fromDate, toDate) = ResolveDateRange(request.From, request.To);
        var fromUtc = DateTime.SpecifyKind(fromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtcExclusive = DateTime.SpecifyKind(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var serviceTypeFilter = ResolveServiceType(request.ServiceType);
        var sourceFilter = ResolveSourceType(request.Source);

        var timeline = await usageEventRepository.GetDailyTimelineAsync(user.Id, fromUtc, toUtcExclusive, serviceTypeFilter?.Id, request.TripVaultUniqueId, sourceFilter?.Id, cancellationToken);
        var (eventItems, totalCount) = await usageEventRepository.GetPagedEventsAsync(user.Id, fromUtc, toUtcExclusive, serviceTypeFilter?.Id, request.TripVaultUniqueId, sourceFilter?.Id, request.Page, request.PageSize, cancellationToken);

        var (currentUsage, monthlyLimit) = await tierLimitService.GetUserTokenStatusAsync(user, cancellationToken);
        var remainingTokens = Math.Max(0, monthlyLimit - currentUsage);

        var timelineItems = BuildTimeline(request.GroupBy, timeline);

        var events = eventItems.Select(MapEventItem).ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize);
        return Result.Success( new GetUsageEventsResponseDTO(new UsageEventsSummaryDTO(currentUsage, monthlyLimit, remainingTokens), timelineItems, events, new UsagePaginationDTO(request.Page, request.PageSize, totalCount, totalPages)));
    }

    private static (DateOnly From, DateOnly To) ResolveDateRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue)
            return (from.Value, to.Value);

        if (from.HasValue)
            return (from.Value, from.Value.AddDays(29));

        if (to.HasValue)
            return (to.Value.AddDays(-29), to.Value);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (today.AddDays(-29), today);
    }

    private static ServiceType? ResolveServiceType(string? serviceType) => string.IsNullOrWhiteSpace(serviceType) ? null : ServiceType.GetAllServices().FirstOrDefault(type => string.Equals(type.Name, serviceType.Trim(), StringComparison.OrdinalIgnoreCase));

    private static UsageEventSourceType? ResolveSourceType(string? source) => string.IsNullOrWhiteSpace(source) ? null : UsageEventSourceType.GetAllSources().FirstOrDefault(sourceType => string.Equals(sourceType.Name, source.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<UsageTimelinePointDTO> BuildTimeline(string groupBy, IReadOnlyList<UsageDailyTimelinePoint> timeline) =>
        ! string.Equals(groupBy, "week", StringComparison.OrdinalIgnoreCase)
            ? timeline
                .Select(point => new UsageTimelinePointDTO(
                    DateOnly.FromDateTime(point.DateUtc),
                    point.TokensConsumed,
                    point.EventsCount))
                .ToList()
            : timeline
                .GroupBy(point => StartOfWeek(point.DateUtc))
                .OrderBy(group => group.Key)
                .Select(group => new UsageTimelinePointDTO(
                    DateOnly.FromDateTime(group.Key),
                    group.Sum(item => item.TokensConsumed),
                    group.Sum(item => item.EventsCount)))
                .ToList();

    private static DateTime StartOfWeek(DateTime dateUtc)
    {
        var dayOfWeek = dateUtc.DayOfWeek;
        var delta = dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
        return dateUtc.AddDays(-delta).Date;
    }

    private static UsageEventItemDTO MapEventItem(UsageEventListItem item)
    {
        var serviceType = ServiceType.GetAllServices().FirstOrDefault(type => type.Id == item.ServiceTypeId)?.Name
            ?? item.ServiceTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var source = UsageEventSourceType.GetAllSources().FirstOrDefault(type => type.Id == item.UsageEventSourceId)?.Name
            ?? item.UsageEventSourceId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var tripVault = item.TripVaultUniqueId.HasValue
            && !string.IsNullOrWhiteSpace(item.TripVaultName)
            && !string.Equals(item.TripVaultName, TripVaultConstants.DefaultVault, StringComparison.OrdinalIgnoreCase)
            ? new UsageTripVaultDTO(item.TripVaultUniqueId.Value, item.TripVaultName)
            : null;

        return new UsageEventItemDTO(item.UniqueId, item.OccurredAt, serviceType, source, item.TokensConsumed, tripVault);
    }
}
