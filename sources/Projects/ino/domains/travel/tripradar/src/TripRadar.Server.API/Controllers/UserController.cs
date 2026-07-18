using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.API.Contracts.Responses.Update;
using TripRadar.Server.API.Filters;
using TripRadar.Server.API.Security;
using TripRadar.Server.API.Services;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.UseCases.Users.Commands.ActivateUser;
using TripRadar.Server.Application.UseCases.Users.Commands.BindTelegramChat;
using TripRadar.Server.Application.UseCases.Users.Commands.ChangePassword;
using TripRadar.Server.Application.UseCases.Users.Commands.ConfirmEmail;
using TripRadar.Server.Application.UseCases.Users.Commands.CreateNewUser;
using TripRadar.Server.Application.UseCases.Users.Commands.DeleteCurrentUser;
using TripRadar.Server.Application.UseCases.Users.Commands.ForgotPassword;
using TripRadar.Server.Application.UseCases.Users.Commands.Logout;
using TripRadar.Server.Application.UseCases.Users.Commands.ResendEmailConfirmation;
using TripRadar.Server.Application.UseCases.Users.Commands.ResetPassword;
using TripRadar.Server.Application.UseCases.Users.Commands.SyncTelegramUsername;
using TripRadar.Server.Application.UseCases.Users.Commands.UnsubscribeMarketingEmails;
using TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserProfile;
using TripRadar.Server.Application.UseCases.Users.Queries.GetUserProfileByUsername;
using TripRadar.Server.Application.UseCases.Users.Queries.GetUserTierUsage;
using TripRadar.Server.Infrastructure.Contracts.Authentication;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/users")]
[RequireUsername]
public class UserController(IMediator mediator, IMapper mapper, IOptions<EmailSettings> emailSettings, IHostEnvironment hostEnvironment, IAuthenticationService authenticationService, IAuthResponseBuilder authResponseBuilder, ITelegramChatNotifier telegramChatNotifier) : BaseController
{
    [AllowAnonymous]
    [HttpPost]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest userRequest, CancellationToken cancellationToken = default)
    {
        var createResult = await mediator.Send(mapper.Map<CreateNewUserCommand>(userRequest) with { IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() }, cancellationToken);
        return createResult.IsFailure ? BadRequest(createResult.Error) : StatusCode(StatusCodes.Status201Created, Created());
    }

    [Authorize]
    [HttpGet("tier-usages")]
    [ProducesResponseType(typeof(GetUserTierUsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTierUsage(CancellationToken cancellationToken = default)
    {
        var tierUsageResult = await mediator.Send(new GetUserTierUsageQuery(GetUsername()), cancellationToken);
        return tierUsageResult.IsFailure ? BadRequest(tierUsageResult.Error) : Ok(mapper.Map<GetUserTierUsageResponse>(tierUsageResult.Value));
    }

    [AllowAnonymous]
    [HttpGet("email-confirmations")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult RedirectEmailConfirmation([FromQuery] string email, [FromQuery] string token)
    {
        var redirectBaseUrl = emailSettings.Value.RedirectUrl.TrimEnd('/');
        var fragment = $"email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        return Redirect($"{redirectBaseUrl}/confirm-email#{fragment}");
    }

    [AllowAnonymous]
    [HttpPost("email-confirmations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request, CancellationToken cancellationToken = default)
    {
        var confirmationResult = await mediator.Send(new ConfirmEmailCommand(request.Email, request.Token), cancellationToken);
        return confirmationResult.IsFailure ? BadRequest(confirmationResult.Error) : Ok();
    }

    [AllowAnonymous]
    [HttpPost("email-confirmation-requests")]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendEmailConfirmation([FromBody] ResendEmailConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        var resendResult = await mediator.Send(mapper.Map<ResendEmailConfirmationCommand>(request), cancellationToken);
        return resendResult.IsFailure ? BadRequest(resendResult.Error) : Ok();
    }

    [AllowAnonymous]
    [HttpPost("password-reset-requests")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var forgotPasswordResult = await mediator.Send(mapper.Map<ForgotPasswordCommand>(request), cancellationToken);
        return forgotPasswordResult.IsFailure ? BadRequest(forgotPasswordResult.Error) : Ok();
    }

    [AllowAnonymous]
    [HttpPost("password-resets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var resetPasswordResult = await mediator.Send(mapper.Map<ResetPasswordCommand>(request), cancellationToken);
        return resetPasswordResult.IsFailure ? BadRequest(resetPasswordResult.Error) : Ok();
    }

    [Authorize]
    [HttpPut("password")]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var changePasswordResult = await mediator.Send(mapper.Map<ChangePasswordCommand>(request) with { Username = GetUsername() }, cancellationToken);
        return changePasswordResult.IsFailure ? BadRequest(changePasswordResult.Error) : Ok();
    }

    [Authorize]
    [HttpDelete]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCurrentUser(CancellationToken cancellationToken = default)
    {
        var deleteResult = await mediator.Send(new DeleteCurrentUserCommand(GetUsername()), cancellationToken);
        return deleteResult.IsFailure ? HandleError(deleteResult.Error) : Ok();
    }

    [Authorize]
    [HttpGet("profile")]
    [ProducesResponseType(typeof(GetUserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserProfile(CancellationToken cancellationToken = default)
    {
        var profileResult = await mediator.Send(new GetUserProfileByUsernameQuery(GetUsername()), cancellationToken);
        return profileResult.IsFailure ? BadRequest(profileResult.Error) : Ok(mapper.Map<GetUserProfileResponse>(profileResult.Value));
    }

    [Authorize]
    [HttpPut("profile")]
    [ProducesResponseType(typeof(UpdateUserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserProfile([FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        var updateResult = await mediator.Send(mapper.Map<UpdateUserProfileCommand>(request) with { Username = GetUsername() }, cancellationToken);
        return updateResult.IsFailure ? BadRequest(updateResult.Error) : Ok(mapper.Map<UpdateUserProfileResponse>(updateResult.Value));
    }

    [Authorize]
    [HttpPatch("profile/telegram")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> UpdateUserTelegramProfile([FromBody] UpdateUserTelegramProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TelegramUserId <= 0)
            return BadRequest(Errors.TelegramAuthInvalid);

        var username = GetUsername();
        var linkResult = await mediator.Send(new BindTelegramChatCommand(username, request.TelegramUserId), cancellationToken);
        if (linkResult.IsFailure)
            return HandleError(linkResult.Error);

        var delivered = await telegramChatNotifier.NotifySignedInAsync(request.TelegramUserId, username, cancellationToken);
        return delivered
            ? Ok(new { delivered = true })
            : StatusCode(StatusCodes.Status502BadGateway, new { delivered = false });
    }
    
    [AllowAnonymous]
    [HttpPatch("marketing-emails/unsubscribe")]
    [ProducesResponseType(typeof(UserManagementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnsubscribeMarketingEmails([FromQuery] string? username, [FromQuery] string? email, CancellationToken cancellationToken = default)
    {
        var unsubscribeResult = await mediator.Send(new UnsubscribeMarketingEmailsCommand(username, email), cancellationToken);
        return unsubscribeResult.IsFailure ? HandleError(unsubscribeResult.Error) : Ok();
    }

    [AllowAnonymous]
    [HttpPatch("activation")]
    [ProducesResponseType(typeof(ActivateUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser([FromBody] ActivateUserRequest request, CancellationToken cancellationToken = default)
    {
        var telegramAuthDto = mapper.Map<Application.DTO.Models.TelegramAuthDataDTO>(request.TelegramAuth);
        var activateResult = await mediator.Send(new ActivateUserCommand(request.Email, request.TelegramAuth.Username, telegramAuthDto), cancellationToken);

        if (activateResult.IsFailure) return HandleError(activateResult.Error);

        var tokenResult = await authenticationService.GetTokenByTelegramAuthAsync(telegramAuthDto);
        return tokenResult.IsFailure ? HandleError(tokenResult.Error) : Ok(authResponseBuilder.BuildActivationResponse(HttpContext, tokenResult.Value?.Token, tokenResult.Value?.RefreshToken, request.Email, request.TelegramAuth.Username));
    }

    [AllowAnonymous]
    [HttpPatch("telegram-username-sync")]
    [ProducesResponseType(typeof(ActivateUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncTelegramUsername([FromBody] SyncTelegramUsernameRequest request, CancellationToken cancellationToken = default)
    {
        var telegramAuthDto = mapper.Map<Application.DTO.Models.TelegramAuthDataDTO>(request.TelegramAuth);
        var syncResult = await mediator.Send(new SyncTelegramUsernameCommand(telegramAuthDto), cancellationToken);
        if (syncResult.IsFailure)
            return HandleError(syncResult.Error);

        var tokenResult = await authenticationService.GetTokenByTelegramAuthAsync(telegramAuthDto);
        return tokenResult.IsFailure ? HandleError(tokenResult.Error) : Ok(authResponseBuilder.BuildActivationResponse(HttpContext, tokenResult.Value?.Token, tokenResult.Value?.RefreshToken, syncResult.Value.Email, syncResult.Value.Username));
    }

    [Authorize]
    [HttpPost("portable-session")]
    [ProducesResponseType(typeof(GetLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePortableSession(CancellationToken cancellationToken = default)
    {
        var tokenResult = await authenticationService.GetTokenByUsernameAsync(GetUsername());
        return tokenResult.IsFailure ? HandleError(tokenResult.Error) : Ok(authResponseBuilder.BuildLoginResponse(HttpContext, tokenResult.Value?.Token, tokenResult.Value?.RefreshToken));
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        var logoutResult = await mediator.Send(new LogoutCommand(GetUsername()), cancellationToken);
        if (logoutResult.IsFailure) return HandleError(logoutResult.Error);

        AuthCookieHelper.ClearAuthCookies(Response, hostEnvironment);
        return Ok();
    }
}
