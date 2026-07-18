using MediatR;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.Login;

public class LoginCommandHandler(ILoginOrchestrator loginOrchestrator)
    : IRequestHandler<LoginCommand, Result<AuthenticationModel>>
{
    public Task<Result<AuthenticationModel>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        return loginOrchestrator.LoginAsync(request.UsernameOrEmail, request.Password, cancellationToken);
    }
}
