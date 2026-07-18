using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler(
    IUnitOfWork unitOfWork,
    IEmailService emailService,
    IRecoveryTokenHasher recoveryTokenHasher,
    ILogger<ForgotPasswordCommandHandler> logger) : IRequestHandler<ForgotPasswordCommand, Result>
{
    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user == null || !user.IsActive)
        {
            return Result.Success();
        }

        var resetToken = JwtExtensions.GenerateToken();
        var tokenExpiry = DateTime.UtcNow.AddHours(1);

        user.SetPasswordResetToken(recoveryTokenHasher.Hash(resetToken), tokenExpiry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var username = string.IsNullOrWhiteSpace(user.Profile.Username) ? user.Profile.Email : user.Profile.Username;
        var emailSent = await emailService.SendPasswordResetAsync(request.Email, username, resetToken, user.Profile.Language?.LanguageCode, cancellationToken);
        if (!emailSent)
        {
            logger.LogWarning("Password reset was requested for user {UserId}, but the email was not sent. Check EmailService logs.", user.Id);
        }

        return Result.Success();
    }
}
