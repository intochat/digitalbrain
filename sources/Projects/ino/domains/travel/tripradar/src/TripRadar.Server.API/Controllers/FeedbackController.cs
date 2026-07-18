using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Filters;
using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Responses;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.UseCases.Feedbacks.Commands.CreateFeedback;
using TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetAllFeedbacks;
using TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetFeedbackCategories;
using TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetUserFeedback;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/feedbacks")]
[RequireUsername]
public class FeedbackController(IMediator mediator, IMapper mapper) : BaseController
{
    [HttpPost("user")]
    [ProducesResponseType(typeof(GetUserFeedbackResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateFeedback([FromBody] CreateFeedbackRequest request, CancellationToken ct = default)
    {
        var feedbackCategoryType = Enumeration.GetAll<FeedbackCategoryType>().SingleOrDefault(i => i.Id == (int)request.FeedbackCategoryType);

        if (feedbackCategoryType == null)
            return BadRequest(Errors.InvalidFeedbackCategory);

        var result = await mediator.Send(new CreateFeedbackCommand(GetUsername(), request.Title, request.Content, request.Rating, feedbackCategoryType), ct);
        if (result.IsFailure)
            return HandleError(result.Error);

        var feedbackResponse = mapper.Map<GetUserFeedbackResponse>((Request: request, FeedbackCategoryType: feedbackCategoryType, CreatedOn: DateTime.UtcNow));

        return CreatedAtAction(nameof(GetUserFeedback), null, feedbackResponse);
    }

    [HttpGet("user")]
    [ProducesResponseType(typeof(IEnumerable<GetUserFeedbackResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserFeedback(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUserFeedbackQuery(GetUsername()), ct);
        return !result.IsSuccess ? HandleError(result.Error) : Ok(mapper.Map<IEnumerable<GetUserFeedbackResponse>>(result.Value));
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PaginatedResponse<GetAllFeedbacksResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllFeedbacks([FromQuery, Range(1, int.MaxValue)] int pageNumber = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAllFeedbacksQuery(pageNumber, pageSize), ct);
        if (!result.IsSuccess)
            return HandleError(result.Error);

        var feedbackPage = result.Value!;

        return Ok(mapper.Map<PaginatedResponse<GetAllFeedbacksResponse>>(feedbackPage));
    }

    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GetFeedbackCategoriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFeedbackCategories(CancellationToken ct)
    {
        var result = await mediator.Send(new GetFeedbackCategoriesQuery(), ct);
        return !result.IsSuccess ? HandleError(result.Error) : Ok(new GetFeedbackCategoriesResponse { Categories = mapper.Map<List<FeedbackCategory>>(result.Value) });
    }
}
