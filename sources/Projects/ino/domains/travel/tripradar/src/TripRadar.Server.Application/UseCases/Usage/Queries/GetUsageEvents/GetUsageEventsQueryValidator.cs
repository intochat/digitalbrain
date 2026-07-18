using FluentValidation;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Usage.Queries.GetUsageEvents;

public sealed class GetUsageEventsQueryValidator : AbstractValidator<GetUsageEventsQuery>
{
    private const int MaxDateRangeDays = 365;

    private static readonly HashSet<string> _allowedGroupByValues = ["day", "week"];
    private static readonly HashSet<string> _allowedServiceTypes = ServiceType.GetAllServices()
        .Select(type => type.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _allowedSources = UsageEventSourceType.GetAllSources()
        .Select(source => source.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public GetUsageEventsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(ValidationConstants.MinLimit, ValidationConstants.MaxLimit);

        RuleFor(query => query.GroupBy)
            .Must(groupBy => _allowedGroupByValues.Contains(groupBy.Trim().ToLowerInvariant()))
            .WithMessage("groupBy must be either 'day' or 'week'.");

        RuleFor(query => query.ServiceType)
            .Must(serviceType => string.IsNullOrWhiteSpace(serviceType) || _allowedServiceTypes.Contains(serviceType.Trim()))
            .WithMessage("serviceType is invalid.");

        RuleFor(query => query.Source)
            .Must(source => string.IsNullOrWhiteSpace(source) || _allowedSources.Contains(source.Trim()))
            .WithMessage("source is invalid.");

        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From.Value <= query.To.Value)
            .WithMessage("from must be less than or equal to to.");

        RuleFor(query => query)
            .Must(query =>
            {
                if (!query.From.HasValue || !query.To.HasValue)
                {
                    return true;
                }

                return query.To.Value.DayNumber - query.From.Value.DayNumber <= MaxDateRangeDays;
            })
            .WithMessage($"Date range cannot exceed {MaxDateRangeDays} days.");
    }
}
