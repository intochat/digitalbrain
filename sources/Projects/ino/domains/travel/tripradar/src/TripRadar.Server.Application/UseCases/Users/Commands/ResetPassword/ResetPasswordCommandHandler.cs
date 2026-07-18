using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ResetPassword;

public class ResetPasswordCommandHandler(IUnitOfWork unitOfWork, IRecoveryTokenHasher recoveryTokenHasher) : IRequestHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetAuthByUsernameAsync(request.Username, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Errors.UserNotFound);
        }

        if (!user.IsActive)
        {
            return Result.Failure(Errors.UserDisabled);
        }

        if (!user.Profile.PasswordResetTokenExpiry.HasValue ||
            user.Profile.PasswordResetTokenExpiry.Value <= DateTime.UtcNow ||
            !recoveryTokenHasher.Verify(request.Token, user.Profile.PasswordResetToken))
        {
            return Result.Failure(Errors.InvalidPasswordResetToken);
        }

        user.ResetPassword(request.NewPassword);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
