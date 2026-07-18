namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IAuthTokenProvider
{
    string? Token { get; }
}