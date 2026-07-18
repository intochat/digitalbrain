using MediatR;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GoogleLogin;

public class GoogleLoginCommandHandler(
    IGoogleAuthenticationOrchestrator googleAuthenticationOrchestrator,
    IAuthenticationTokenIssuer tokenIssuer)
    : IRequestHandler<GoogleLoginCommand, Result<AuthenticationModel>>
{
    public Task<Result<AuthenticationModel>> Handle(GoogleLoginCommand request, CancellationToken cancellationToken) =>
        googleAuthenticationOrchestrator.HandleGoogleLoginAsync(request.Email, request.FirstName, request.LastName, request.GoogleId, request.ProfilePictureUrl, tokenIssuer.IssueTokensAsync);
}
