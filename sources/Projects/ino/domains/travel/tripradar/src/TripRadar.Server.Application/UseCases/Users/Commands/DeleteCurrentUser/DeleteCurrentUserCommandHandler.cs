using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.DeleteCurrentUser;

public class DeleteCurrentUserCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<DeleteCurrentUserCommand, Result>
{
    public async Task<Result> Handle(DeleteCurrentUserCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Errors.UserNotFound);
        }

        await unitOfWork.UserRepository.DeleteAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
