using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Filters;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.DeleteScheduledExecution;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionConfiguration;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionQuery;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutionSearchTypes;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutions;
using TripRadar.Server.Comms.Core.Extensions;
using QueryColumn = TripRadar.Server.Domain.ValueObjects.QueryColumn;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/scheduled-executions")]
public class ScheduledExecutionController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpGet]
    [RequireUsername]
    [ProducesResponseType(typeof(GetScheduledExecutionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> GetScheduledExecutions(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetScheduledExecutionsQuery(GetUsername()), ct);

        if (result.IsFailure)
            return HandleError(result.Error);

        return Ok(new GetScheduledExecutionsResponse
        {
            ScheduledExecutions = mapper.Map<List<ScheduledExecutionItem>>(result.Value)
        });
    }

    [HttpGet("search-types")]
    [ProducesResponseType(typeof(GetScheduledExecutionSearchTypesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult?> GetScheduledExecutionSearchTypes(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetScheduledExecutionSearchTypesQuery(), ct);
        return result.IsFailure
            ? HandleError(result.Error)
            : Ok(new GetScheduledExecutionSearchTypesResponse { SearchTypes = result.Value.ToList() });
    }

    [HttpDelete("{uniqueId}")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> DeleteScheduledExecution([FromRoute] Guid uniqueId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new DeleteScheduledExecutionCommand(uniqueId, GetUsername()), ct);
        return result.IsFailure ? HandleError(result.Error) : NoContent();
    }

    [HttpPatch("{uniqueId}/query")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> UpdateQuery([FromRoute] Guid uniqueId, [FromBody] UpdateScheduledExecutionQueryRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateScheduledExecutionQueryCommand(
            uniqueId,
            GetUsername(),
            request.SearchQuery,
            request.Location,
            request.Radius,
            request.DepartureAirportCode,
            request.DestinationAirportCode,
            request.DepartureDate,
            request.ReturnDate,
            request.CheckInDate,
            request.CheckOutDate,
            request.SelectedColumns?.Select(c => new QueryColumn(c.Name, c.IsActive)).ToList(),
            request.AdditionalParameters.SerializeParameters()), ct);
        return result.IsFailure ? HandleError(result.Error) : NoContent();
    }

    [HttpPatch("{uniqueId}/configuration")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult?> UpdateConfiguration([FromRoute] Guid uniqueId, [FromBody] UpdateScheduledExecutionConfigurationRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateScheduledExecutionConfigurationCommand(uniqueId, GetUsername(), request.IsActive, request.Schedule, request.NextExecutionTime), ct);
        return result.IsFailure ? HandleError(result.Error) : NoContent();
    }
}
