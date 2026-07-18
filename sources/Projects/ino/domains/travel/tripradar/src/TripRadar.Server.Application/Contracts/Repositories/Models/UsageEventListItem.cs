namespace TripRadar.Server.Application.Contracts.Repositories.Models;

public sealed record UsageEventListItem(
    Guid UniqueId,
    DateTime OccurredAt,
    int ServiceTypeId,
    int UsageEventSourceId,
    decimal TokensConsumed,
    Guid? TripVaultUniqueId,
    string? TripVaultName);
