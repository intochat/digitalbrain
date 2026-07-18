using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.ToggleSubscription;

public sealed class ToggleSubscriptionCommandHandler(IUnitOfWork unitOfWork, IStripeGateway stripeGateway) : IRequestHandler<ToggleSubscriptionCommand, Result<ToggleSubscriptionDTO>>
{
    public async Task<Result<ToggleSubscriptionDTO>> Handle(ToggleSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByUsernameWithSubscriptionAsync(request.Username, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ToggleSubscriptionDTO>(Errors.UserNotFound);
        }

        var userSubscription = user.UserSubscription;
        if (string.IsNullOrWhiteSpace(userSubscription?.StripeSubscriptionId))
        {
            return Result.Failure<ToggleSubscriptionDTO>(Errors.SubscriptionNotFound);
        }

        var subscription = await stripeGateway.ToggleSubscriptionAsync(userSubscription.StripeSubscriptionId, request.Activate, cancellationToken);

        var action = request.Activate ? "reactivated" : "scheduled for cancellation";
        return Result.Success(new ToggleSubscriptionDTO
        {
            Message = $"Subscription {action} successfully",
            Status = subscription.Status
        });
    }
}
