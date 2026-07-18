using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.SyncTelegramUsername;

public class SyncTelegramUsernameCommandHandler(
    IUnitOfWork unitOfWork,
    IUserLookupService userLookupService,
    ITelegramAuthValidationService telegramAuthValidationService)
    : IRequestHandler<SyncTelegramUsernameCommand, Result<(string Email, string Username)>>
{
    public async Task<Result<(string Email, string Username)>> Handle(
        SyncTelegramUsernameCommand request,
        CancellationToken cancellationToken)
    {
        if (!telegramAuthValidationService.Validate(request.TelegramAuth) || request.TelegramAuth.Id <= 0)
            return Result.Failure<(string Email, string Username)>(Errors.TelegramAuthInvalid);

        var requestedUsername = request.TelegramAuth.Username;
        if (string.IsNullOrWhiteSpace(requestedUsername))
            return Result.Failure<(string Email, string Username)>(Errors.UsernameRequired);

        var userResult = await userLookupService.FindByTelegramUserIdAsync(request.TelegramAuth.Id, cancellationToken);
        if (userResult.IsFailure)
            return Result.Failure<(string Email, string Username)>(userResult.Error);

        var user = userResult.Value!;

        var existingUser = await unitOfWork.UserRepository.GetByUsernameAsync(requestedUsername, cancellationToken);
        if (existingUser != null && existingUser.Id != user.Id)
            return Result.Failure<(string Email, string Username)>(Errors.UserExists);

        user.UpdateUsername(requestedUsername);
        user.UpdatePersonalInfo(request.TelegramAuth.FirstName, request.TelegramAuth.LastName, user.Profile.PhoneNumber);
        user.UpdateTelegramUserId(request.TelegramAuth.Id);

        // Invalidate all previously issued sessions and rotate tokens after username sync.
        user.ClearRefreshToken();
        user.RotateSecurityStamp();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success((user.Profile.Email, requestedUsername));
    }
}
