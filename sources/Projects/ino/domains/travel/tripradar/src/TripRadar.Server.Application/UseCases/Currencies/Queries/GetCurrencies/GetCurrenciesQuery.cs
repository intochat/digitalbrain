using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Currencies.Queries.GetCurrencies;

public record GetCurrenciesQuery : IRequest<Result<IEnumerable<CurrencyResponseDTO>>>;
