using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetUserFeedback;

public class GetUserFeedbackQueryHandler(IFeedbackRepository feedbackRepository, ICurrentUserContext currentUserContext)
    : IRequestHandler<GetUserFeedbackQuery, Result<IEnumerable<Feedback>>>
{
    public async Task<Result<IEnumerable<Feedback>>> Handle(GetUserFeedbackQuery request, CancellationToken cancellationToken)
    {
        var username = currentUserContext.User?.Profile.Username;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Failure<IEnumerable<Feedback>>(Errors.UnauthorizedAccess);
        }

        var feedback = await feedbackRepository.GetUserFeedbacksAsync(username, cancellationToken);
        return Result.Success(feedback);
    }
}
