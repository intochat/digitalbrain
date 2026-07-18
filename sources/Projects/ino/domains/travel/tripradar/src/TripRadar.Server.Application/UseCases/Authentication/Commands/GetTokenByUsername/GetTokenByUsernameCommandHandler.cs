using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByUsername;

public class GetTokenByUsernameCommandHandler(
    IUnitOfWork unitOfWork,
    IUserLookupService userLookupService,
    IAuthenticationTokenIssuer tokenIssuer,
    IUserAccessValidator userAccessValidator)
    : IRequestHandler<GetTokenByUsernameCommand, Result<AuthenticationModel>>
{
    public async Task<Result<AuthenticationModel>> Handle(GetTokenByUsernameCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Username))
            return Result.Failure<AuthenticationModel>(Errors.UsernameRequired);

        var userResult = await userLookupService.FindByUsernameAsync(request.Username, cancellationToken);
        if (userResult.IsFailure)
            return Result.Failure<AuthenticationModel>(userResult.Error);

        var user = userResult.Value;
        var accessValidationResult = userAccessValidator.Validate(user!);
        if (accessValidationResult.IsFailure)
        {
            return Result.Failure<AuthenticationModel>(accessValidationResult.Error);
        }

        return await tokenIssuer.IssueTokensAsync(user!, scope);
    }
}
