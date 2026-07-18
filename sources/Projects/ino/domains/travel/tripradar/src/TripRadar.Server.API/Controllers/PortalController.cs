using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.UseCases.Airlines.Queries.GetAirlines;
using TripRadar.Server.Application.UseCases.Currencies.Queries.GetCurrencies;
using TripRadar.Server.Application.UseCases.Languages.Queries.GetLanguages;
using TripRadar.Server.Application.UseCases.Timezones.Queries.GetTimezones;

namespace TripRadar.Server.API.Controllers;

[AllowAnonymous]
[Route("api/v{version:apiVersion}/portal")]
public class PortalController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpGet("languages")]
    [ProducesResponseType(typeof(GetLanguagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetLanguages(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLanguagesQuery(), ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new GetLanguagesResponse { Languages = (result.Value ?? throw new InvalidOperationException()).Select(mapper.Map<LanguageResponse>) });
    }

    [HttpGet("airlines")]
    [ProducesResponseType(typeof(GetAirlinesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAirlines([FromQuery] string? query = null, [FromQuery] int limit = 500, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAirlinesQuery(query, limit), ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new GetAirlinesResponse { Airlines = (result.Value ?? throw new InvalidOperationException()).Select(mapper.Map<AirlineResponse>) });
    }

    [HttpGet("currencies")]
    [ProducesResponseType(typeof(GetCurrenciesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCurrencies(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCurrenciesQuery(), ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new GetCurrenciesResponse { Currencies = (result.Value ?? throw new InvalidOperationException()).Select(mapper.Map<CurrencyResponse>) });
    }

    [HttpGet("timezones")]
    [ProducesResponseType(typeof(GetTimezonesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTimezones(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTimezonesQuery(), ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new GetTimezonesResponse { Timezones = (result.Value ?? throw new InvalidOperationException()).Select(mapper.Map<TimezoneResponse>) });
    }
}
