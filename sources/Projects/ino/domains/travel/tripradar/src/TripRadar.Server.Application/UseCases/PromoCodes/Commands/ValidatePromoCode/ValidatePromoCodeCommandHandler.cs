using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.ValidatePromoCode;

public class ValidatePromoCodeCommandHandler(
    IUnitOfWork unitOfWork,
    IPromoCodeValidationService promoCodeValidationService) : IRequestHandler<ValidatePromoCodeCommand, Result>
{
    public async Task<Result> Handle(ValidatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var user = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
            if (user is null)
                return Result.Failure(Errors.UserNotFound);

            var promoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);

            if (promoCode is null)
                return Result.Failure(Errors.PromoCodeNotFound);

            var validationResult = await promoCodeValidationService.ValidatePromoCodeForUserAsync(promoCode, user.Id, cancellationToken);
            if (validationResult.IsFailure)
                return validationResult;

            await scope.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}
