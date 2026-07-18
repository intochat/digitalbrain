using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.API.Filters;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.UseCases.Preferences.Queries.GetAllPreferenceTypes;
using TripRadar.Server.Application.UseCases.Preferences.Queries.GetPreferenceCategories;
using TripRadar.Server.Application.UseCases.Preferences.Queries.GetPreferenceTypesByService;
using TripRadar.Server.Application.UseCases.Preferences.Queries.GetServices;
using TripRadar.Server.Application.UseCases.Users.Commands.UpdatePrivacyMode;
using TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserPreferences;
using TripRadar.Server.Application.UseCases.Users.Queries.GetPrivacyMode;
using TripRadar.Server.Application.UseCases.Users.Queries.GetUserPreferences;
using TripRadar.Server.Domain.Enums;
using PreferenceType = TripRadar.Server.API.Contracts.Models.PreferenceType;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/preferences")]
public class PreferencesController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpGet("services")]
    [ProducesResponseType(typeof(GetServicesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPreferenceServices(CancellationToken ct)
    {
        var result = await mediator.Send(new GetServicesQuery(), ct);
        return result.IsSuccess ? Ok(new GetServicesResponse { Services = mapper.Map<List<ServiceInfo>>(result.Value) }) : BadRequest(result.Error);
    }

    [HttpGet("types")]
    [ProducesResponseType(typeof(GetPreferenceTypesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPreferenceTypes(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllPreferenceTypesQuery(), ct);
        return result.IsSuccess ? Ok(new GetPreferenceTypesResponse { PreferenceTypes = mapper.Map<List<PreferenceType>>(result.Value) }) : BadRequest(result.Error);
    }

    [HttpGet("services/{serviceType}/types")]
    [ProducesResponseType(typeof(GetPreferenceTypesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPreferenceTypesByService(Contracts.Enums.ServiceType serviceType, CancellationToken ct)
    {
        var result = await mediator.Send(new GetPreferenceTypesByServiceQuery(mapper.Map<ServiceType>(serviceType)), ct);
        return result.IsSuccess ? Ok(new GetPreferenceTypesResponse { PreferenceTypes = mapper.Map<List<PreferenceType>>(result.Value) }) : BadRequest(result.Error);
    }

    [HttpGet("categories")]
    [ProducesResponseType(typeof(GetPreferenceCategoriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPreferenceCategories(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPreferenceCategoriesQuery(), ct);
        return result.IsSuccess ? Ok(mapper.Map<GetPreferenceCategoriesResponse>(result.Value)) : HandleError(result.Error);
    }

    [HttpGet("user")]
    [RequireUsername]
    [ProducesResponseType(typeof(GetUserPreferencesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserPreferences(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUserPreferencesQuery(GetUsername()), ct);
        return result.IsSuccess ? Ok(mapper.Map<GetUserPreferencesResponse>(result.Value)) : HandleError(result.Error);
    }

    [HttpGet("user/privacy-mode")]
    [RequireUsername]
    [ProducesResponseType(typeof(GetPrivacyModeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserPreferencePrivacyMode(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPrivacyModeQuery(GetUsername()), ct);
        return result.IsSuccess ? Ok(new GetPrivacyModeResponse { Enabled = result.Value }) : HandleError(result.Error);
    }

    [HttpPut("user")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserPreferences([FromBody] Contracts.Requests.Update.UpdateUserPreferencesRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateUserPreferencesCommand(GetUsername(), mapper.Map<UserPreferencePatchRequestDTO>(request.Preferences)), ct);
        return result.IsSuccess ? Ok() : HandleError(result.Error);
    }

    [HttpPut("user/privacy-mode")]
    [RequireUsername]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserPreferencePrivacyMode([FromBody] Contracts.Requests.Update.UpdatePrivacyModeRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdatePrivacyModeCommand(GetUsername(), request.Enabled), ct);
        return result.IsSuccess ? Ok() : HandleError(result.Error);
    }
}
