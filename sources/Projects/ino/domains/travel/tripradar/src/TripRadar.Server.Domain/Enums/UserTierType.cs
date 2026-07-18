using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class UserTierType(int id, string name) : Enumeration(id, name)
{
    public static readonly UserTierType Basic = new(1, nameof(Basic));
    public static readonly UserTierType Essential = new(2, nameof(Essential));
    public static readonly UserTierType Advanced = new(3, nameof(Advanced));
}
