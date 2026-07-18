using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Enums;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.UseCases.TripVaults.Commands.CreateTripVault;
using TripRadar.Server.Application.UseCases.TripVaults.Commands.DeleteTripVault;
using TripRadar.Server.Application.UseCases.TripVaults.Commands.RemoveTripItem;
using TripRadar.Server.Application.UseCases.TripVaults.Commands.UpdateTripVault;
using TripRadar.Server.Application.UseCases.TripVaults.Queries.GetRecentTripSearches;
using TripRadar.Server.Application.UseCases.TripVaults.Queries.GetTripDetails;
using TripRadar.Server.Application.UseCases.TripVaults.Queries.GetTripQueryHistory;
using TripRadar.Server.Application.UseCases.TripVaults.Queries.GetUserTrips;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/trips")]
public class TripVaultController(IMediator mediator, IMapper mapper, IUnitOfWork unitOfWork, ICurrentRequestUserProvider currentRequestUserProvider, ITripQueryHistorySummaryExpander tripQueryHistorySummaryExpander) : BaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TripVaultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserTrips(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUserTripsQuery(GetUsername()), ct);
        if (!result.IsSuccess)
            return HandleError(result.Error);

        var vaults = result.Value.ToList();
        var responses = mapper.Map<List<TripVaultResponse>>(vaults);

        if (vaults.Count <= 0)
            return Ok(responses);

        var counts = await unitOfWork.TripVaultRepository.GetItemsCountByVaultIdsAsync(vaults.Select(v => v.Id), ct);
        foreach (var (vault, response) in vaults.Zip(responses))
            response.ItemsCount = counts.GetValueOrDefault(vault.Id, 0);

        return Ok(responses);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TripVaultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateTripVault([FromBody] CreateTripVaultRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateTripVaultCommand(GetUsername(), request.Name, request.Description, request.StartDate, request.EndDate), ct);

        if (result.IsFailure)
            return HandleError(result.Error);

        var response = mapper.Map<TripVaultResponse>(request);
        response.UniqueId = result.Value;

        return CreatedAtAction(nameof(GetTripDetails), new { uniqueId = result.Value }, response);
    }

    [HttpGet("{uniqueId:guid}")]
    [ProducesResponseType(typeof(TripVaultDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripDetails([FromRoute] Guid uniqueId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTripDetailsQuery(GetUsername(), uniqueId), ct);
        return !result.IsSuccess ? HandleError(result.Error) : Ok(mapper.Map<TripVaultDetailsResponse>(result.Value));
    }

    [HttpPut("{uniqueId:guid}")]
    [ProducesResponseType(typeof(TripVaultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTripVault([FromRoute] Guid uniqueId, [FromBody] UpdateTripVaultRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateTripVaultCommand(GetUsername(), uniqueId, request.Name, request.Description, request.StartDate, request.EndDate), ct);

        if (result.IsFailure)
            return HandleError(result.Error);

        var response = mapper.Map<TripVaultResponse>(request);
        response.UniqueId = result.Value;

        return Ok(response);
    }

    [HttpDelete("{uniqueId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTripVault([FromRoute] Guid uniqueId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new DeleteTripVaultCommand(GetUsername(), uniqueId), ct);
        return !result.IsSuccess ? HandleError(result.Error) : NoContent();
    }

    [HttpDelete("{uniqueId:guid}/items/{itemId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveTripItem([FromRoute] Guid uniqueId, [FromRoute] long itemId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RemoveTripItemCommand(GetUsername(), uniqueId, itemId), ct);
        return !result.IsSuccess ? HandleError(result.Error) : NoContent();
    }

    [HttpDelete("{uniqueId:guid}/items/by-unique-id/{itemUniqueId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveTripItemByUniqueId([FromRoute] Guid uniqueId, [FromRoute] Guid itemUniqueId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RemoveTripItemByUniqueIdCommand(GetUsername(), uniqueId, itemUniqueId), ct);
        return !result.IsSuccess ? HandleError(result.Error) : NoContent();
    }

    [HttpGet("{uniqueId:guid}/history")]
    [ProducesResponseType(typeof(TripQueryHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripQueryHistory([FromRoute] Guid uniqueId, [FromQuery, Range(1, int.MaxValue)] int pageNumber = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTripQueryHistoryQuery(GetUsername(), uniqueId, pageNumber, pageSize), ct);

        if (!result.IsSuccess)
            return HandleError(result.Error);

        var response = new TripQueryHistoryResponse
        {
            Items = mapper.Map<List<TripItemResponse>>(result.Value.Items),
            TotalCount = result.Value.TotalCount,
            Page = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(result.Value.TotalCount / (double)pageSize)
        };

        await tripQueryHistorySummaryExpander.ExpandAsync(response.Items, ct);
        return Ok(response);
    }

    [HttpGet("recent-searches")]
    [ProducesResponseType(typeof(GetRecentSearchesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRecentSearches([FromQuery] ServiceType serviceType, [FromQuery, Range(1, 5)] int limit = 3, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetRecentTripSearchesQuery(GetUsername(), mapper.Map<Domain.Enums.ServiceType>(serviceType), limit), ct);
        if (!result.IsSuccess)
            return HandleError(result.Error);

        return Ok(new GetRecentSearchesResponse
        {
            RecentSearches = mapper.Map<List<RecentSearchItemResponse>>(result.Value)
        });
    }
}
