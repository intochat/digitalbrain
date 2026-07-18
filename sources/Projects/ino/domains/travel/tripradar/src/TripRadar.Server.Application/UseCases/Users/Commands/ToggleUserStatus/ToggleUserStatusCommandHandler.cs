using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ToggleUserStatus;

public class ToggleUserStatusCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ToggleUserStatusCommand, Result>
{
    public async Task<Result> Handle(ToggleUserStatusCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Errors.UserNotFound);
        }

        user.ToggleStatus();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
