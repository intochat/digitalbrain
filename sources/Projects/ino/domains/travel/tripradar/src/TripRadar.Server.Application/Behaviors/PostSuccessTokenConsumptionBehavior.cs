using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Behaviors;

public class PostSuccessTokenConsumptionBehavior<TRequest, TResponse>(
    IUserLimitService userLimitService,
    ICurrentUserContext currentUserContext,
    ILogger<PostSuccessTokenConsumptionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITokenConsumingRequest
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var user = currentUserContext.User;
        if (user is null)
        {
            return ResultResponseFactory<TResponse>.CreateFailure(new Error("USER_NOT_FOUND", "Current user context is not available."));
        }

        var prepareResult = await userLimitService.PrepareTokenConsumptionAsync(
            user,
            request.ServiceType,
            cancellationToken);

        if (prepareResult.IsFailure)
        {
            return ResultResponseFactory<TResponse>.CreateFailure(prepareResult.Error);
        }

        if (prepareResult.Value is not { } ticket)
        {
            logger.LogError(
                "Token consumption preparation succeeded but returned an empty ticket for user {UserId}, service {ServiceType}",
                user.Id,
                request.ServiceType.Name);
            return ResultResponseFactory<TResponse>.CreateFailure(Errors.InternalServerError);
        }

        try
        {
            var response = await next(cancellationToken);

            if (IsSuccessResponse(response))
            {
                var commitResult = await userLimitService.CommitTokenConsumptionAsync(user, ticket);
                if (commitResult.IsFailure)
                {
                    logger.LogError(
                        "Failed to commit token consumption for user {UserId}, service {ServiceType}: {ErrorCode} - {ErrorMessage}",
                        user.Id,
                        request.ServiceType.Name,
                        commitResult.Error.Code,
                        commitResult.Error.Reason);
                }
            }
            else
            {
                await userLimitService.RollbackTokenConsumptionAsync(user, ticket, cancellationToken);
            }

            return response;
        }
        catch
        {
            await userLimitService.RollbackTokenConsumptionAsync(user, ticket, cancellationToken);
            throw;
        }
    }

    private static bool IsSuccessResponse(TResponse response)
    {
        if (response is Result result)
        {
            return result.IsSuccess;
        }

        return true;
    }
}
