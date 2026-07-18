namespace TripRadar.Server.Infrastructure.Contracts;

public interface IRefreshTokenHasher
{
    string Hash(string refreshToken);

    bool Verify(string refreshToken, string storedValue, out bool isLegacy);
}
