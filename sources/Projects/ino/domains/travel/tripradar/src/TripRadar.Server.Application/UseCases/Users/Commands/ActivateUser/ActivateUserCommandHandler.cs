using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ActivateUser;

public class ActivateUserCommandHandler(
    IUnitOfWork unitOfWork,
    ITelegramAuthValidationService telegramAuthValidationService,
    ILogger<ActivateUserCommandHandler> logger) : IRequestHandler<ActivateUserCommand, Result>
{
    public async Task<Result> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        if (!telegramAuthValidationService.Validate(request.TelegramAuth))
        {
            return Result.Failure(Errors.TelegramAuthInvalid);
        }

        var user = await unitOfWork.UserRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Errors.EmailNotFound);
        }

        var existingUser = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (existingUser != null && existingUser.Id != user.Id)
        {
            return Result.Failure(Errors.UserExists);
        }
        user.UpdateUsername(request.Username);
        user.UpdatePersonalInfo(request.TelegramAuth.FirstName, request.TelegramAuth.LastName, user.Profile.PhoneNumber);
        user.UpdateTelegramUserId(request.TelegramAuth.Id);
        user.Activate();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(new StringBuilder()
                .Append("User hasn't been activated. The following exception is happened: ")
                .Append(exception.Message)
                .Append("With inner exception: ")
                .Append(exception.InnerException?.Message)
                .ToString());

            return Result.Failure(Errors.UserExists);
        }

        return Result.Success();
    }
}
