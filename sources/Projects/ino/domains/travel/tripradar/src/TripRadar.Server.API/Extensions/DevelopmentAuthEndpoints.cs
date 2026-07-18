using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.API.Extensions;

public static class DevelopmentAuthEndpoints
{
    public static void MapDevelopmentAuthEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapPost("/api/v1/tokens/dev", IssueDevelopmentTokenAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> IssueDevelopmentTokenAsync(
        [FromBody] CreateDevLoginRequest request,
        HttpContext httpContext,
        ITelegramAuthenticationService telegramAuthenticationService,
        IAuthenticationTokenIssuer tokenIssuer,
        IUnitOfWork unitOfWork,
        IUserAccessValidator userAccessValidator,
        IAuthResponseBuilder authResponseBuilder,
        CancellationToken cancellationToken)
    {
        if (request.TelegramUserId <= 0)
        {
            return ToErrorResult(Errors.TelegramAuthInvalid);
        }

        if (!TryResolveTierId(request.Tier, out var tierId))
        {
            return Results.BadRequest(new
            {
                errorCode = "INVALID_TIER",
                errorReason = "Tier must be one of: basic, essential, advanced."
            });
        }

        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        var upsertResult = await telegramAuthenticationService.UpsertUserAsync(CreateTelegramAuthData(request.TelegramUserId), cancellationToken);
        if (upsertResult.IsFailure)
        {
            return ToErrorResult(upsertResult.Error);
        }

        var user = upsertResult.Value!;
        user.UpdateTier(tierId);
        ApplyDevelopmentSubscriptionState(user, tierId);

        var accessValidationResult = userAccessValidator.Validate(user);
        if (accessValidationResult.IsFailure)
        {
            return ToErrorResult(accessValidationResult.Error);
        }

        var tokenResult = await tokenIssuer.IssueTokensAsync(user, scope);
        if (tokenResult.IsFailure)
        {
            return ToErrorResult(tokenResult.Error);
        }

        var response = authResponseBuilder.BuildLoginResponse(
            httpContext,
            tokenResult.Value?.Token,
            tokenResult.Value?.RefreshToken);

        return Results.Ok(response);
    }

    private static TelegramAuthDataDTO CreateTelegramAuthData(long telegramUserId) => new()
    {
        Id = telegramUserId,
        Username = $"tg_{telegramUserId}",
        FirstName = "Dev",
        LastName = "User",
        AuthDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Hash = "development"
    };

    private static void ApplyDevelopmentSubscriptionState(Domain.Aggregates.User user, int tierId)
    {
        if (tierId == UserTierType.Basic.Id)
        {
            user.UserSubscription?.Deactivate();
            return;
        }

        if (user.UserSubscription is null)
        {
            _ = new UserSubscription(user);
        }
        else
        {
            user.UserSubscription.Activate();
        }

        user.UserSubscription!.UpdateSubscriptionExpirationTime(null);
        user.UserSubscription.UpdatePendingTier(null);
        user.UserSubscription.SetDeferredDowngradeJobId(null);
        user.UserSubscription.SetPayAsYouGo(false);
    }

    private static bool TryResolveTierId(string? tier, out int tierId)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            tierId = UserTierType.Basic.Id;
            return true;
        }

        switch (tier.Trim().ToLowerInvariant())
        {
            case "basic":
                tierId = UserTierType.Basic.Id;
                return true;
            case "essential":
                tierId = UserTierType.Essential.Id;
                return true;
            case "advanced":
                tierId = UserTierType.Advanced.Id;
                return true;
            default:
                tierId = 0;
                return false;
        }
    }

    private static IResult ToErrorResult(TripRadar.Server.Comms.Core.Errors.Error error)
    {
        var errorResponse = new
        {
            errorCode = error.Code,
            errorReason = error.Reason
        };

        return error.Code switch
        {
            var code when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => Results.NotFound(errorResponse),
            "UNAUTHORIZED" => Results.Unauthorized(),
            "FORBIDDEN" => Results.StatusCode(StatusCodes.Status403Forbidden),
            "UNAUTHORIZED_ACCESS" => Results.Json(errorResponse, statusCode: StatusCodes.Status403Forbidden),
            "FEEDBACK_RATE_LIMIT_EXCEEDED" => Results.Json(errorResponse, statusCode: StatusCodes.Status429TooManyRequests),
            var code when code.EndsWith("_UNAUTHORIZED_ACCESS", StringComparison.Ordinal) => Results.Json(errorResponse, statusCode: StatusCodes.Status403Forbidden),
            _ => Results.BadRequest(errorResponse)
        };
    }
}
