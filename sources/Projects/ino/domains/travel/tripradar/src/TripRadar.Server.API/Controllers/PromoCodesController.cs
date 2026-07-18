using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Filters;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.ApplyPromoCode;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.ValidatePromoCode;
using TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeByCode;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/promo-codes")]
public class PromoCodesController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpPost("validations")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidatePromoCode([FromBody] ValidatePromoCodeRequest request, CancellationToken ct = default)
    {

        var result = await mediator.Send(new ValidatePromoCodeCommand(request.Code, GetUsername()), ct);
        return result.IsFailure ? HandleError(result.Error) : Created();
    }

    [HttpPost("applications")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyPromoCode([FromBody] ApplyPromoCodeRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApplyPromoCodeCommand(request.Code, GetUsername(), request.OrderAmount), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok();
    }

    [HttpGet("{code}")]
    [ProducesResponseType(typeof(GetPromoCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPromoCodeByCode([FromRoute] string code, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPromoCodeByCodeQuery(code), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(mapper.Map<GetPromoCodeResponse>(result.Value));
    }
}
