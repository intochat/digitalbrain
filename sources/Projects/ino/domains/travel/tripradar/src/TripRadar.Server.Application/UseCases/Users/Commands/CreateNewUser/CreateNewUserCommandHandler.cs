using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Users.Commands.CreateNewUser;

public class CreateNewUserCommandHandler(
    IUnitOfWork unitOfWork,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IEmailService emailService,
    IRecoveryTokenHasher recoveryTokenHasher,
    ILogger<CreateNewUserCommandHandler> logger) : IRequestHandler<CreateNewUserCommand, Result>
{
    public async Task<Result> Handle(CreateNewUserCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        if (!request.HasDataStorageConsent)
        {
            return Result.Failure(Errors.UserConsentNotGranted);
        }

        var emailExists = await unitOfWork.UserRepository.EmailExistsAsync(request.Email, cancellationToken);
        if (emailExists)
        {
            return Result.Failure(Errors.UserExists);
        }

        if (string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return Result.Failure(Errors.UserIpNotValidOrNotProvided);
        }

        var user = User.Register(request.Password, request.Email, request.HasDataStorageConsent, request.FirstName, request.LastName, request.PhoneNumber, request.IpAddress, tierId: UserTierType.Basic.Id);
        var emailConfirmationToken = JwtExtensions.GenerateToken();
        var tokenExpiry = DateTime.UtcNow.AddDays(1);
        user.SetEmailConfirmationToken(recoveryTokenHasher.Hash(emailConfirmationToken), tokenExpiry);

        await unitOfWork.UserRepository.CreateAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
        await userMonthlyTokenCountRepository.CreateMonthlyTokenCountsAsync(user, now.Year, now.Month, user.Profile.TimezoneCode, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        var emailSent = await emailService.SendEmailConfirmationAsync(request.Email, emailConfirmationToken, cancellationToken);

        if (!emailSent)
        {
            logger.LogWarning("User {UserId} was created, but the confirmation email was not sent. Check EmailService logs.", user.Id);
        }

        return Result.Success();
    }
}
