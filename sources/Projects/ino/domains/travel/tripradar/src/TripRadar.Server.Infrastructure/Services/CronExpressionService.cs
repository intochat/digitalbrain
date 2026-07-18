using Cronos;
using Microsoft.Extensions.Caching.Memory;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;

namespace TripRadar.Server.Infrastructure.Services;

public class CronExpressionService : ICronExpressionService
{
    private static readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });

    public DateTime? GetNextOccurrence(string schedule)
    {
        var cronExpression = GetOrParseCronExpression(schedule);
        return cronExpression.GetNextOccurrence(DateTime.UtcNow);
    }

    private static CronExpression GetOrParseCronExpression(string schedule) =>
        _cache.GetOrCreate(schedule, entry =>
        {
            entry.SetSize(1);
            entry.SetSlidingExpiration(TimeSpan.FromHours(6));
            return CronExpression.Parse(schedule);
        })!;
}
