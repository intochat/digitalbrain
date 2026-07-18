namespace TripRadar.Server.Infrastructure.Contracts;

public interface IBlindIndexService
{
    string? ComputeHash(string? value);
}
