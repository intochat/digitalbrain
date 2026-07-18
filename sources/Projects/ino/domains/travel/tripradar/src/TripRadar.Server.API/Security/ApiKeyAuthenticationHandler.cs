using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TripRadar.Server.API.Contracts;

namespace TripRadar.Server.API.Security;

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyValidator apiKeyValidator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string ServiceUsername = "api-service";
    public const string ApiKeyHeaderName = "X-API-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Defer to JWT when a Bearer token is present
        if (Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey)
            || extractedApiKey.Count != 1
            || string.IsNullOrWhiteSpace(extractedApiKey[0]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!apiKeyValidator.IsValid(extractedApiKey[0]))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, ServiceUsername),
            new Claim(ClaimTypes.Role, "Service"),
            new Claim(ClaimTypes.AuthenticationMethod, SchemeName)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
