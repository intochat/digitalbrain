namespace TripRadar.Server.API.Contracts;

public interface ICurrentRequestUserProvider
{
    bool TryGetUserId(out long userId);

    bool TryGetUsername(out string username);
}
