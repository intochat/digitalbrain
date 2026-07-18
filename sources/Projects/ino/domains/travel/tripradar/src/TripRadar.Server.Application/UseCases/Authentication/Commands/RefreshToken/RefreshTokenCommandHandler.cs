using MediatR;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.RefreshToken;

public class RefreshTokenCommandHandler(IRefreshTokenOrchestrator refreshTokenOrchestrator)
    : IRequestHandler<RefreshTokenCommand, Result<AuthenticationModel>>
{
    public Task<Result<AuthenticationModel>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        return refreshTokenOrchestrator.RefreshAsync(request.UserId, request.RefreshToken, cancellationToken);
    }
}
