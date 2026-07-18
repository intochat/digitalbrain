using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeUsageHistory;

public class GetPromoCodeUsageHistoryQueryHandler(IUnitOfWork unitOfWork, IPromoCodeUsageRepository promoCodeUsageRepository) : IRequestHandler<GetPromoCodeUsageHistoryQuery, Result<List<PromoCodeUsage>>>
{
    public async Task<Result<List<PromoCodeUsage>>> Handle(GetPromoCodeUsageHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var promoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);
            return promoCode is null
                ? Result.Failure<List<PromoCodeUsage>>(Errors.PromoCodeNotFound)
                : Result.Success(await promoCodeUsageRepository.GetUsagesByPromoCodeAsync(promoCode.Id, cancellationToken));
        }
        catch (Exception ex)
        {
            return Result.Failure<List<PromoCodeUsage>>(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}
