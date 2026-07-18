namespace TripRadar.Server.Application.Contracts.Repositories.Models;

public sealed record UsageDailyTimelinePoint(DateTime DateUtc, decimal TokensConsumed, int EventsCount);
