namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface ICredentialValidator
{
    bool IsValidTokenFormat(string token);

    bool IsEmail(string input);
}
