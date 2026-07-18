using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using DomainDiscountType = TripRadar.Server.Domain.Enums.DiscountType;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.CreatePromoCode;

public class CreatePromoCodeCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreatePromoCodeCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var existingPromoCode = await unitOfWork.PromoCodeRepository.GetByCodeAsync(request.Code, cancellationToken);
            if (existingPromoCode is not null)
            {
                return Result.Failure<long>(Errors.PromoCodeAlreadyExists);
            }

            if (request.DiscountType.Id != DomainDiscountType.Percentage.Id &&
                request.DiscountType.Id != DomainDiscountType.FixedAmount.Id)
            {
                return Result.Failure<long>(Errors.InvalidDiscountType);
            }

            if (request.DiscountType.Id == DomainDiscountType.Percentage.Id && request.DiscountValue is <= 0 or > 100)
            {
                return Result.Failure<long>(Errors.InvalidPercentage);
            }

            if (request.DiscountType.Id == DomainDiscountType.FixedAmount.Id && request.DiscountValue <= 0)
            {
                return Result.Failure<long>(Errors.InvalidFixedAmount);
            }

            var promoCode = new PromoCode(
                request.Code,
                request.Description,
                request.DiscountType.Id,
                request.DiscountValue,
                request.MaxUsageCount,
                request.MaxUsagePerUser,
                request.StartDate,
                request.EndDate,
                request.IsActive
            );

            await unitOfWork.PromoCodeRepository.AddAsync(promoCode, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            return Result.Success(promoCode.Id);
        }
        catch (Exception ex)
        {
            return Result.Failure<long>(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}
