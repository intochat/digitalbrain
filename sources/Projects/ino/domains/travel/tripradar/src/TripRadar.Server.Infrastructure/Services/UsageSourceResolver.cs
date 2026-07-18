using Microsoft.AspNetCore.Http;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public class UsageSourceResolver(IHttpContextAccessor httpContextAccessor) : IUsageSourceResolver
{
    public UsageEventSourceType ResolveCurrentSource()
    {
        var path = httpContextAccessor.HttpContext?.Request?.Path.Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            return UsageEventSourceType.Scheduled;
        }

        if (path.Contains("telegram", StringComparison.OrdinalIgnoreCase))
        {
            return UsageEventSourceType.Telegram;
        }

        return UsageEventSourceType.Api;
    }
}
