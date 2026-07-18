using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ChangePassword;

public class ChangePasswordCommandHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext) : IRequestHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!user.ChangePassword(request.CurrentPassword, request.NewPassword))
        {
            return Result.Failure(Errors.CurrentPasswordIncorrect);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
