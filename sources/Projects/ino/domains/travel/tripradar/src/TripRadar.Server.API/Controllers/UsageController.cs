using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Filters;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.UseCases.Usage.Queries.GetUsageEvents;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/usage")]
[RequireUsername]
public class UsageController(IMediator mediator, IMapper mapper) : BaseController
{
    [Authorize]
    [HttpGet("events")]
    [ProducesResponseType(typeof(GetUsageEventsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUsageEvents([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string groupBy = "day", [FromQuery] string? serviceType = null, [FromQuery] Guid? tripVaultUniqueId = null, [FromQuery] string? source = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUsageEventsQuery(GetUsername(), from, to, groupBy, serviceType, tripVaultUniqueId, source, page, pageSize), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(mapper.Map<GetUsageEventsResponse>(result.Value));
    }
}
