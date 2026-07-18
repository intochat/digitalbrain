using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.BindTelegramChat;

public class BindTelegramChatCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<BindTelegramChatCommand, Result>
{
    public async Task<Result> Handle(BindTelegramChatCommand request, CancellationToken cancellationToken)
    {
        if (request.TelegramUserId <= 0)
            return Result.Failure(Errors.TelegramAuthInvalid);

        var user = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (user is null)
            return Result.Failure(Errors.UserNotFound);

        if (user.Profile.TelegramUserId == request.TelegramUserId)
            return Result.Success();

        user.UpdateTelegramUserId(request.TelegramUserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
