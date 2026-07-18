using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.DeletePromoCode;

public class DeletePromoCodeCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<DeletePromoCodeCommand, Result>
{
    public async Task<Result> Handle(DeletePromoCodeCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var promoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);

            if (promoCode is null)
            {
                return Result.Failure(Errors.PromoCodeNotFound);
            }

            promoCode.Deactivate();
            promoCode.MarkAsDeleted();

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
