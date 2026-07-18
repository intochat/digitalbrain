using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.UpdatePromoCode;

public class UpdatePromoCodeCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdatePromoCodeCommand, Result>
{
    public async Task<Result> Handle(UpdatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var promoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);

            if (promoCode is null)
            {
                return Result.Failure(Errors.PromoCodeNotFound);
            }

            promoCode.Update(
                request.Description,
                request.MaxUsageCount,
                request.MaxUsagePerUser,
                request.StartDate,
                request.EndDate,
                request.IsActive
            );

            await unitOfWork.PromoCodeRepository.UpdatePromoCodeAsync(promoCode, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}
