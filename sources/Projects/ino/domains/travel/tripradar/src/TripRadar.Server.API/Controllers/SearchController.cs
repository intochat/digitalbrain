using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.UseCases.Airports.Queries.SearchAirports;
using TripRadar.Server.Application.UseCases.Locations.Queries.SearchLocations;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/search")]
public sealed class SearchController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpGet("airports")]
    [ProducesResponseType(typeof(GetAirportSuggestionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult?> SearchAirportsAsync(
        [FromQuery] string query,
        [FromQuery, Range(1, 20)] int limit = 10,
        [FromQuery] string? hl = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new SearchAirportsQuery(query, limit, hl), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(mapper.Map<GetAirportSuggestionsResponse>(result.Value));
    }

    [HttpGet("locations")]
    [ProducesResponseType(typeof(GetLocationSuggestionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult?> SearchLocationsAsync(
        [FromQuery] string query,
        [FromQuery, Range(1, 20)] int limit = 10,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new SearchLocationsQuery(query, limit), ct);
        return result.IsFailure ? HandleError(result.Error) : Ok(mapper.Map<GetLocationSuggestionsResponse>(result.Value));
    }
}
