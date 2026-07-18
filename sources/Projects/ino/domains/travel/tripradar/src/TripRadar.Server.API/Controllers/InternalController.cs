using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.CreatePromoCode;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.DeletePromoCode;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.UpdatePromoCode;
using TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeUsageHistory;
using TripRadar.Server.Application.UseCases.Users.Commands.DeleteUser;
using TripRadar.Server.Application.UseCases.Users.Commands.ToggleUserStatus;
using InternalApiAttribute = TripRadar.Server.Comms.Core.Attributes.InternalAttribute;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.API.Controllers;

[InternalApi]
[Route("api/v{version:apiVersion}/internals")]
public class InternalController(IInternalTokenService internalTokenService, IMapper mapper, IMediator mediator) : BaseController
{
    [HttpDelete("users/{username}")]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteUser([FromRoute] string username, CancellationToken ct = default)
    {
        var result = await mediator.Send(new DeleteUserCommand(username), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(new UserManagementResponse { Message = "User deleted successfully" });
    }

    [HttpPatch("users/{username}/status")]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ToggleUserStatus([FromRoute] string username, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ToggleUserStatusCommand(username), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(new UserManagementResponse { Message = "User status updated successfully" });
    }
    
    [HttpPatch("users/{username}/tokens")]
    [ProducesResponseType(typeof(DeductingTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeductTokens([FromRoute] string username, [FromBody] DeductTokensRequest request, CancellationToken cancellationToken = default)
    {
        var serviceType = Enumeration.GetAll<Domain.Enums.ServiceType>().SingleOrDefault(st => st.Id == (int)request.ServiceType);
        if (serviceType is null)
            return BadRequest(Errors.InvalidServiceType);

        if (!request.RequestSource.HasValue)
            return BadRequest(Errors.InvalidServiceType);

        var sourceType =  Enumeration.GetAll<Domain.Enums.UsageEventSourceType>().SingleOrDefault(source => source.Id == (int)request.RequestSource.Value);
        if (sourceType is null)
            return BadRequest(Errors.InvalidUsageEventSource);

        var result = await internalTokenService.DeductTokensAsync(username, request.TokensToDeduct, serviceType, sourceType, cancellationToken);

        if (result.IsFailure)
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.InvalidTokenAmount.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.InvalidUsageEventSource.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.InsufficientTokens.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.AiFeatureRequiresPaidTier.Code => StatusCode(StatusCodes.Status403Forbidden, result.Error),
                _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
            };

        return Ok(mapper.Map<DeductingTokensResponse>(result.Value));
    }

    [HttpPost("promo-codes")]
    [ProducesResponseType(typeof(CreatePromoCodeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreatePromoCode([FromBody] CreatePromoCodeRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(mapper.Map<CreatePromoCodeCommand>(request), ct);
        return result.IsFailure ? HandleError(result.Error) : StatusCode(StatusCodes.Status201Created, new CreatePromoCodeResponse { Message = "Promo code created successfully" });
    }

    [HttpPut("promo-codes/{code}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdatePromoCode([FromRoute] string code, [FromBody] UpdatePromoCodeRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(mapper.Map<UpdatePromoCodeCommand>((code, request)), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok();
    }

    [HttpDelete("promo-codes/{code}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeletePromoCode([FromRoute] string code, CancellationToken ct = default)
    {
        var result = await mediator.Send(new DeletePromoCodeCommand(code), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok();
    }

    [HttpGet("promo-codes/{code}/usage-history")]
    [ProducesResponseType(typeof(IEnumerable<GetPromoCodeUsageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPromoCodeUsageHistory([FromRoute] string code, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPromoCodeUsageHistoryQuery(code), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(mapper.Map<IEnumerable<GetPromoCodeUsageResponse>>(result.Value));
    }
}
