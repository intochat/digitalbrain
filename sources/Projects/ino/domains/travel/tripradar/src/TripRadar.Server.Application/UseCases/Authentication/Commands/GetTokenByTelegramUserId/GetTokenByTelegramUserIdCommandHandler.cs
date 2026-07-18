using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByTelegramUserId;

public class GetTokenByTelegramUserIdCommandHandler(
    IUnitOfWork unitOfWork,
    IUserLookupService userLookupService,
    IAuthenticationTokenIssuer tokenIssuer,
    IUserAccessValidator userAccessValidator)
    : IRequestHandler<GetTokenByTelegramUserIdCommand, Result<AuthenticationModel>>
{
    public async Task<Result<AuthenticationModel>> Handle(GetTokenByTelegramUserIdCommand request, CancellationToken cancellationToken)
    {
        if (request.TelegramUserId <= 0)
            return Result.Failure<AuthenticationModel>(Errors.TelegramAuthInvalid);

        await using UnitOfWorkTransactionScope scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        Result<User> userResult = await userLookupService.FindByTelegramUserIdAsync(request.TelegramUserId, cancellationToken);
        if (userResult.IsFailure)
            return Result.Failure<AuthenticationModel>(userResult.Error);

        User? user = userResult.Value;
        Result accessValidationResult = userAccessValidator.Validate(user!);
        if (accessValidationResult.IsFailure)
            return Result.Failure<AuthenticationModel>(accessValidationResult.Error);

        return await tokenIssuer.IssueTokensAsync(user!, scope);
    }
}
