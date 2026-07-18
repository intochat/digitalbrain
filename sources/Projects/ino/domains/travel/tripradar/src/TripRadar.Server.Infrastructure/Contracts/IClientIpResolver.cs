namespace TripRadar.Server.Infrastructure.Contracts;

public interface IClientIpResolver
{
    string? GetClientIpAddress();
}
