using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UnsubscribeMarketingEmails;

public sealed class UnsubscribeMarketingEmailsCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UnsubscribeMarketingEmailsCommand, Result>
{
    public async Task<Result> Handle(UnsubscribeMarketingEmailsCommand request, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(request, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Errors.UserNotFound);
        }

        if (!user.AllowsMarketingEmails)
        {
            return Result.Success();
        }

        user.UpdateMarketingEmailPermission(false);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<Domain.Aggregates.User?> ResolveUserAsync(
        UnsubscribeMarketingEmailsCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var userByUsername = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
            if (userByUsername is not null)
            {
                return userByUsername;
            }
        }

        return string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await unitOfWork.UserRepository.GetByEmailAsync(request.Email, cancellationToken);
    }
}
