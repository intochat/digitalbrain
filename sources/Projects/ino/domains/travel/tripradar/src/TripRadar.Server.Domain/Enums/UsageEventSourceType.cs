using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class UsageEventSourceType(int id, string name, string description = "") : Enumeration(id, name)
{
    public string Description { get; } = description;

    public static readonly UsageEventSourceType Api = new(1, nameof(Api), "Direct API request");
    public static readonly UsageEventSourceType Scheduled = new(2, nameof(Scheduled), "Scheduled query execution");
    public static readonly UsageEventSourceType Telegram = new(3, nameof(Telegram), "Telegram-driven request");
    public static readonly UsageEventSourceType Ai = new(4, "AI", "AI-driven request");

    public static IReadOnlyList<UsageEventSourceType> GetAllSources() =>
    [
        Api,
        Scheduled,
        Telegram,
        Ai
    ];
}
