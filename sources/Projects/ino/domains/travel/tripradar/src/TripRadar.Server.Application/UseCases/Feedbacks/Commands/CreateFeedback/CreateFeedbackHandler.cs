using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Commands.CreateFeedback;

public class CreateFeedbackHandler(IUnitOfWork unitOfWork, IFeedbackRepository feedbackRepository, ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateFeedbackCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreateFeedbackCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var recentFeedbackCount = await feedbackRepository.CountUserFeedbackSinceAsync(user.Id, DateTime.UtcNow.AddHours(-1), cancellationToken);

        if (recentFeedbackCount >= 5)
            return Result.Failure<long>(Errors.FeedbackRateLimitExceeded);

        var feedback = new Feedback(user.Id, request.Title, request.Content, request.Rating, request.FeedbackCategoryType);
        await feedbackRepository.AddAsync(feedback, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(feedback.Id);
    }
}
