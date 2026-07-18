namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface IPasswordVerificationService
{
    Task<bool> VerifyAsync(string password, string hash, CancellationToken cancellationToken = default);

    Task ConsumeDummyCheckAsync(string password, CancellationToken cancellationToken = default);
}
