using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Filters;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.Application.UseCases.SearchEngine.Events.Commands.CreateScheduledEventQuery;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Commands.CreateScheduledFlightQuery;
using TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Commands.CreateScheduledHotelQuery;
using TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Commands.CreateScheduledLocalPlacesQuery;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/scheduled-queries")]
[RequireUsername]
public class ScheduledQueriesController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> CreateScheduledEventQuery([FromBody] CreateScheduledEventQueryRequest requestBody, CancellationToken ct = default)
    {
        var result = await mediator.Send(mapper.Map<CreateScheduledEventQueryCommand>(requestBody) with { Username = GetUsername() }, ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new CreateScheduledQueryResponse { ScheduledExecutionUniqueId = result.Value });
    }

    [HttpPost("flights")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> CreateScheduledFlightQuery([FromBody] CreateScheduledFlightQueryRequest requestBody, CancellationToken ct = default)
    {
        var result = await mediator.Send(mapper.Map<CreateScheduledFlightQueryCommand>(requestBody) with { Username = GetUsername() }, ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new CreateScheduledQueryResponse { ScheduledExecutionUniqueId = result.Value });
    }

    [HttpPost("hotels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> CreateScheduledHotelQuery([FromBody] CreateScheduledHotelQueryRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(mapper.Map<CreateScheduledHotelQueryCommand>(request) with { Username = GetUsername() }, ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new CreateScheduledQueryResponse { ScheduledExecutionUniqueId = result.Value });
    }

    [HttpPost("local-places")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> CreateScheduledLocalPlacesQuery([FromBody] CreateScheduledLocalPlacesQueryRequest requestBody, CancellationToken ct = default)
    {
        var result = await mediator.Send(mapper.Map<CreateScheduledLocalPlacesQueryCommand>(requestBody) with { Username = GetUsername() }, ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new CreateScheduledQueryResponse { ScheduledExecutionUniqueId = result.Value });
    }
}
