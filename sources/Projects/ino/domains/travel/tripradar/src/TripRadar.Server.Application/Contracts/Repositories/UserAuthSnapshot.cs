namespace TripRadar.Server.Application.Contracts.Repositories;

public sealed record UserAuthSnapshot(bool IsActive, string? SecurityStamp);
