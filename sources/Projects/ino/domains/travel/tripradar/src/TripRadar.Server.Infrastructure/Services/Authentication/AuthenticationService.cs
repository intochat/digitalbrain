using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByTelegramUserId;
using TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByUsername;
using TripRadar.Server.Application.UseCases.Authentication.Commands.GoogleLogin;
using TripRadar.Server.Application.UseCases.Authentication.Commands.Login;
using TripRadar.Server.Application.UseCases.Authentication.Commands.RefreshToken;
using TripRadar.Server.Application.UseCases.Authentication.Commands.TelegramLogin;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Contracts.Authentication;
using TripRadar.Server.Infrastructure.Models;

namespace TripRadar.Server.Infrastructure.Services.Authentication;

public class AuthenticationService(IMediator mediator) : IAuthenticationService
{
    public Task<Result<AuthenticationModel>> LoginAsync(TokenModel model) =>
        mediator.Send(new LoginCommand(model.UsernameOrEmail, model.Password));

    public Task<Result<AuthenticationModel>> GetRefreshTokenAsync(long userId, string refreshToken) =>
        mediator.Send(new RefreshTokenCommand(userId, refreshToken));

    public Task<Result<AuthenticationModel>> GoogleLoginAsync(string email, string firstName, string lastName, string googleId, string? profilePictureUrl) =>
        mediator.Send(new GoogleLoginCommand(email, firstName, lastName, googleId, profilePictureUrl));

    public Task<Result<AuthenticationModel>> GetTokenByTelegramAuthAsync(TelegramAuthDataDTO authData) =>
        mediator.Send(new TelegramLoginCommand(authData));

    public Task<Result<AuthenticationModel>> GetTokenByTelegramUserIdAsync(long telegramUserId) =>
        mediator.Send(new GetTokenByTelegramUserIdCommand(telegramUserId));

    public Task<Result<AuthenticationModel>> GetTokenByUsernameAsync(string username) =>
        mediator.Send(new GetTokenByUsernameCommand(username));
}
