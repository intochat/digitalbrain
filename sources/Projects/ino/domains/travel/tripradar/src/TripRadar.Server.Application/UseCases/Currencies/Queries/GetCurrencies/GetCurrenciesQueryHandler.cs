using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Currencies.Queries.GetCurrencies;

public sealed class GetCurrenciesQueryHandler(ICurrencyRepository currencyRepository) : IRequestHandler<GetCurrenciesQuery, Result<IEnumerable<CurrencyResponseDTO>>>
{
    public async Task<Result<IEnumerable<CurrencyResponseDTO>>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var currencies = await currencyRepository.GetAllAsync(cancellationToken);

        var currencyResponseDtos = currencies.Select(c => new CurrencyResponseDTO(
            CurrencyCode: c.CurrencyCode,
            CurrencyName: c.CurrencyName))
            .OrderBy(c => c.CurrencyCode);

        return Result.Success(currencyResponseDtos.AsEnumerable());
    }
}
