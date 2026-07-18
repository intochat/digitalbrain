namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface IRecoveryTokenHasher
{
    string Hash(string token);

    bool Verify(string token, string? storedValue);
}
