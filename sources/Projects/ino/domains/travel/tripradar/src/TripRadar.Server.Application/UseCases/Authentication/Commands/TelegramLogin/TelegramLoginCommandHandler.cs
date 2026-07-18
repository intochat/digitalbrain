using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByTelegramUserId;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.TelegramLogin;

public class TelegramLoginCommandHandler(ITelegramAuthValidationService telegramAuthValidationService, ISender sender) : IRequestHandler<TelegramLoginCommand, Result<AuthenticationModel>>
{
    public async Task<Result<AuthenticationModel>> Handle(TelegramLoginCommand request, CancellationToken cancellationToken)
    {
        var authData = request.AuthData;
        if (!telegramAuthValidationService.Validate(authData) || authData.Id <= 0)
            return Result.Failure<AuthenticationModel>(Errors.TelegramAuthInvalid);

        return await sender.Send(new GetTokenByTelegramUserIdCommand(authData.Id), cancellationToken);
    }
}
