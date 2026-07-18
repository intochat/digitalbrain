using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.Logout;

public class LogoutCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetAuthByUsernameAsync(request.Username, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Errors.UserNotFound);
        }

        user.ClearRefreshToken();
        user.RotateSecurityStamp();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
