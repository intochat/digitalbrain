using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeByCode;

public class GetPromoCodeByCodeQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPromoCodeByCodeQuery, Result<PromoCode>>
{
    public async Task<Result<PromoCode>> Handle(GetPromoCodeByCodeQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var promoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);
            return promoCode is null ? Result.Failure<PromoCode>(Errors.PromoCodeNotFound) : Result.Success(promoCode);
        }
        catch (Exception ex)
        {
            return Result.Failure<PromoCode>(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}
