using System.Transactions;
using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.ApplyPromoCode;

public class ApplyPromoCodeCommandHandler(IUnitOfWork unitOfWork, IPromoCodeUsageRepository promoCodeUsageRepository) : IRequestHandler<ApplyPromoCodeCommand, Result<decimal>>
{
    public async Task<Result<decimal>> Handle(ApplyPromoCodeCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(
            isolationLevel: IsolationLevel.Serializable,
            cancellationToken: cancellationToken);

        try
        {
            var user = await unitOfWork.UserRepository.GetByUsernameAsync(request.Username, cancellationToken);
            if (user is null)
            {
                return Result.Failure<decimal>(Errors.UserNotFound);
            }

            var promoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);
            if (promoCode is null)
            {
                return Result.Failure<decimal>(Errors.PromoCodeNotFound);
            }

            var userUsageCount = await promoCodeUsageRepository.GetUsageCountByUserAsync(promoCode.Id, user.Id, cancellationToken);
            var discountResult = promoCode.Apply(user.Id, request.OrderAmount, userUsageCount, DateTime.UtcNow);
            if (discountResult.IsFailure)
            {
                return Result.Failure<decimal>(discountResult.Error.ToApplicationError());
            }

            var usage = new PromoCodeUsage(promoCode.Id, user.Id, discountResult.Value!);
            await promoCodeUsageRepository.AddAsync(usage, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            return Result.Success(discountResult.Value);
        }
        catch (Exception ex)
        {
            return Result.Failure<decimal>(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}
