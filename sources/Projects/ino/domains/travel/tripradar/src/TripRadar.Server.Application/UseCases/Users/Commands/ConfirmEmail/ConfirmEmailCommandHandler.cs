using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ConfirmEmail;

public class ConfirmEmailCommandHandler(IUnitOfWork unitOfWork, IRecoveryTokenHasher recoveryTokenHasher) : IRequestHandler<ConfirmEmailCommand, Result>
{
    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Errors.UserNotFound);
        }

        if (user.Profile.IsEmailConfirmed)
        {
            return Result.Failure(Errors.EmailAlreadyConfirmed);
        }

        if (!user.Profile.EmailConfirmationTokenExpiry.HasValue ||
            user.Profile.EmailConfirmationTokenExpiry.Value <= DateTime.UtcNow ||
            !recoveryTokenHasher.Verify(request.Token, user.Profile.EmailConfirmationToken))
        {
            return Result.Failure(Errors.InvalidEmailConfirmationToken);
        }

        user.ConfirmEmail();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
