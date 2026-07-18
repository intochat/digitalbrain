using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IUsageSourceResolver
{
    UsageEventSourceType ResolveCurrentSource();
}
