using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Infrastructure.Contracts.Authentication;
using TripRadar.Server.Infrastructure.Models;

namespace TripRadar.Server.API.Controllers;

[AllowAnonymous]
[Route("api/v{version:apiVersion}/tokens")]
public class TokenController(
    IAuthenticationService authenticationService,
    IMapper mapper,
    IGoogleIdTokenValidator googleIdTokenValidator,
    ITelegramInitDataParser telegramInitDataParser,
    IRefreshTokenRequestResolver refreshTokenRequestResolver,
    IAuthResponseBuilder authResponseBuilder)
    : BaseController
{
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(GetLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTokenAsync([FromBody] CreateLoginRequest request)
    {
        var loginResult = await authenticationService.LoginAsync(mapper.Map<TokenModel>(request));
        if (!loginResult.IsFailure)
            return Ok(authResponseBuilder.BuildLoginResponse(HttpContext, loginResult.Value?.Token, loginResult.Value?.RefreshToken));

        if (loginResult.Error.Code == Errors.EmailNotConfirmed.Code)
            return StatusCode(StatusCodes.Status403Forbidden, new { errorCode = Errors.EmailNotConfirmed.Code });

        return loginResult.Error.Code == Errors.TelegramRequired.Code
            ? StatusCode(StatusCodes.Status403Forbidden, new
            {
                errorCode = Errors.TelegramRequired.Code,
                message = Errors.TelegramRequired.Reason,
                email = loginResult.Error.Reason
            })
            : BadRequest(loginResult.Error);
    }

    [HttpPost("sessions/google")]
    [ProducesResponseType(typeof(GetLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GoogleLoginAsync([FromBody] CreateGoogleLoginRequest request)
    {
        var payload = await googleIdTokenValidator.ValidateAsync(request.IdToken);
        if (string.IsNullOrWhiteSpace(payload?.Email))
            return BadRequest(Errors.InvalidToken);

        var loginResult = await authenticationService.GoogleLoginAsync(payload.Email, payload.GivenName ?? string.Empty, payload.FamilyName ?? string.Empty, payload.Subject, payload.Picture);
        if (loginResult.IsFailure)
            return loginResult.Error.Code == Errors.TelegramRequired.Code
                ? StatusCode(StatusCodes.Status403Forbidden, new
                {
                    errorCode = Errors.TelegramRequired.Code,
                    message = Errors.TelegramRequired.Reason,
                    email = loginResult.Error.Reason
                })
                : BadRequest(loginResult.Error);

        return Ok(authResponseBuilder.BuildLoginResponse(HttpContext, loginResult.Value?.Token, loginResult.Value?.RefreshToken));
    }

    [HttpPost("sessions/telegram")]
    [ProducesResponseType(typeof(GetLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TelegramLoginAsync([FromBody] CreateTelegramSessionRequest request)
    {
        Application.DTO.Models.TelegramAuthDataDTO? authData = null;

        if (request.TelegramAuth is not null)
            authData = mapper.Map<Application.DTO.Models.TelegramAuthDataDTO>(request.TelegramAuth);
        else if (!string.IsNullOrWhiteSpace(request.InitData))
        {
            if (!telegramInitDataParser.TryParse(request.InitData, out var parsedAuthData))
                return BadRequest(Errors.TelegramAuthInvalid);

            authData = parsedAuthData;
        }

        if (authData is null)
            return BadRequest(Errors.TelegramAuthInvalid);

        var loginResult = await authenticationService.GetTokenByTelegramAuthAsync(authData);
        if (loginResult.IsFailure)
        {
            if (loginResult.Error.Code == Errors.EmailNotConfirmed.Code)
                return StatusCode(StatusCodes.Status403Forbidden, new { errorCode = Errors.EmailNotConfirmed.Code });

            return loginResult.Error.Code == Errors.UserNotFound.Code
                ? BadRequest(Errors.TelegramAccountNotLinked)
                : HandleError(loginResult.Error);
        }

        return Ok(authResponseBuilder.BuildLoginResponse(HttpContext, loginResult.Value?.Token, loginResult.Value?.RefreshToken));
    }

    [HttpPost("refresh-tokens")]
    [ProducesResponseType(typeof(GetLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] CreateRefreshTokenRequest request)
    {
        var refreshToken = refreshTokenRequestResolver.ResolveRefreshToken(HttpContext, request);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest(Errors.InvalidToken);

        if (!refreshTokenRequestResolver.TryResolveUserId(HttpContext, request, out var userId))
            return Unauthorized();

        var refreshResult = await authenticationService.GetRefreshTokenAsync(userId, refreshToken);
        if (refreshResult.IsFailure)
            return BadRequest(refreshResult.Error);

        return Ok(authResponseBuilder.BuildLoginResponse(HttpContext, refreshResult.Value?.Token, refreshResult.Value?.RefreshToken));
    }
}
