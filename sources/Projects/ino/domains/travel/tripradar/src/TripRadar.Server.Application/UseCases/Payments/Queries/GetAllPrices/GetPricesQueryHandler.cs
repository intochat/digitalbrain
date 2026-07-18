using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetAllPrices;

public class GetPricesQueryHandler(
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions) : IRequestHandler<GetPricesQuery, Result<List<Payment>>>
{
    private readonly CachingSettings _cachingSettings = cacheOptions.Value;

    public async Task<Result<List<Payment>>> Handle(GetPricesQuery request, CancellationToken cancellationToken)
    {
        if (!_cachingSettings.Enabled)
        {
            var prices = await unitOfWork.PriceRepository.GetAllAsync(cancellationToken);
            return Result.Success(prices.Select(Payment.MapToDto).ToList());
        }

        var cacheKey = _cachingSettings.PricesCache.CacheKey;
        var cachedPayments = await cacheService.GetByKeyAsync<List<Payment>>(cacheKey);

        if (cachedPayments != null)
        {
            return Result.Success(cachedPayments);
        }

        var pricesFromDb = await unitOfWork.PriceRepository.GetAllAsync(cancellationToken);
        var paymentDtos = pricesFromDb.Select(Payment.MapToDto).ToList();

        await cacheService.TrySetAsync(cacheKey, paymentDtos, _cachingSettings.PricesCache.ExpirationHours);

        return Result.Success(paymentDtos);
    }
}
