namespace TripRadar.Server.Application.Contracts.Requests;

public interface IAuthorizedRequest
{
    string Username { get; }
}
