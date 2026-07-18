using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ResendEmailConfirmation;

public class ResendEmailConfirmationCommandHandler(
    IUnitOfWork unitOfWork,
    IEmailService emailService,
    IRecoveryTokenHasher recoveryTokenHasher,
    ILogger<ResendEmailConfirmationCommandHandler> logger)
    : IRequestHandler<ResendEmailConfirmationCommand, Result>
{
    public async Task<Result> Handle(ResendEmailConfirmationCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user == null)
        {
            return Result.Success();
        }

        if (user.Profile.IsEmailConfirmed)
        {
            return Result.Failure(Errors.EmailAlreadyConfirmed);
        }

        var emailConfirmationToken = JwtExtensions.GenerateToken();
        var tokenExpiry = DateTime.UtcNow.AddDays(1);
        user.SetEmailConfirmationToken(recoveryTokenHasher.Hash(emailConfirmationToken), tokenExpiry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var emailSent = await emailService.SendEmailConfirmationAsync(request.Email, emailConfirmationToken, cancellationToken);
        if (!emailSent)
        {
            logger.LogWarning("Resend email confirmation was requested for user {UserId}, but the email was not sent. Check EmailService logs.", user.Id);
        }

        return Result.Success();
    }
}
