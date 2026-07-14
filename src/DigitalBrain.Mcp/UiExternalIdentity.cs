using System.Security.Claims;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Contracts;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record UiExternalIdentityOptions(bool Enabled, string Issuer, string Audience, string SubjectClaim, string GrantsClaim, IReadOnlySet<string> AllowedGrants, bool RequireHttpsMetadata)
{
    public const string AuthenticationScheme = "digitalbrain-ui-external-oidc";
    private const string SectionPath = "DigitalBrain:Runtime:Ui:Oidc";

    public static UiExternalIdentityOptions FromConfiguration(IConfiguration configuration, RuntimeProfile profile)
    {
        var section = configuration.GetSection(SectionPath);
        var issuer = section["Issuer"]?.Trim() ?? string.Empty;
        var audience = section["Audience"]?.Trim() ?? string.Empty;
        var configuredGrants = ReadValues(section.GetSection("AllowedGrants"), section["AllowedGrants"]);
        var anyConfigured = section.GetChildren().Any();
        if (!anyConfigured && profile != RuntimeProfile.Production) return Disabled();
        if (issuer.Length == 0 || audience.Length == 0 || configuredGrants.Length == 0)
            throw new InvalidOperationException($"{SectionPath} issuer, audience, and allowed grants are required.");

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Host.Length == 0 || issuerUri.UserInfo.Length != 0 || issuerUri.Query.Length != 0 || issuerUri.Fragment.Length != 0 ||
            issuer.Length > 512 || profile == RuntimeProfile.Production && issuerUri.Scheme != Uri.UriSchemeHttps ||
            profile != RuntimeProfile.Production && issuerUri.Scheme != Uri.UriSchemeHttps && !issuerUri.IsLoopback)
            throw new InvalidOperationException($"{SectionPath}:Issuer must be an absolute HTTPS issuer (loopback HTTP is development-only).");
        if (!ValidBounded(audience, 512))
            throw new InvalidOperationException($"{SectionPath}:Audience is invalid.");

        var subjectClaim = ClaimName(section["SubjectClaim"], "sub", "SubjectClaim");
        var grantsClaim = ClaimName(section["GrantsClaim"], "digitalbrain_grants", "GrantsClaim");
        if (string.Equals(subjectClaim, grantsClaim, StringComparison.Ordinal))
            throw new InvalidOperationException($"{SectionPath} claim names must be distinct.");

        var grants = configuredGrants.ToHashSet(StringComparer.Ordinal);
        if (grants.Count > 64 || grants.Any(static grant => !ValidBounded(grant, 128)))
            throw new InvalidOperationException($"{SectionPath}:AllowedGrants contains an invalid capability.");
        return new(true, issuer, audience, subjectClaim, grantsClaim, grants, issuerUri.Scheme == Uri.UriSchemeHttps);
    }

    public void Configure(JwtBearerOptions options)
    {
        if (!Enabled) throw new InvalidOperationException("External OIDC authentication is not configured.");
        options.Authority = Issuer;
        options.Audience = Audience;
        options.RequireHttpsMetadata = RequireHttpsMetadata;
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            RequireAudience = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = SubjectClaim
        };
    }

    public bool TryMapPrincipal(ClaimsPrincipal principal, out RuntimeRequestContext context)
    {
        context = default!;
        if (!Enabled || principal is null) return false;
        var identities = principal.Identities.Where(static identity => identity.IsAuthenticated).ToArray();
        if (identities.Length != 1 || !TryUniqueClaim(identities[0], SubjectClaim, out var subject))
            return false;

        var assertedGrants = identities[0].FindAll(GrantsClaim).SelectMany(static claim => claim.Value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (assertedGrants.Length is 0 or > 64 || assertedGrants.Any(static grant => !ValidBounded(grant, 128)) ||
            assertedGrants.Any(grant => !AllowedGrants.Contains(grant)))
            return false;

        context = new RuntimeRequestContext(
            BrainOwnerId.FromExternalIdentity(Issuer, subject),
            ActorId.FromExternalIdentity(Issuer, subject),
            new SessionId("external-oidc-bootstrap"),
            AuthAssurance.Oidc,
            Guid.NewGuid().ToString("N"),
            null,
            assertedGrants.ToHashSet(StringComparer.Ordinal));
        return true;
    }

    private static UiExternalIdentityOptions Disabled() => new(false, string.Empty, string.Empty, "sub", "digitalbrain_grants", new HashSet<string>(StringComparer.Ordinal), true);

    private static string ClaimName(string? configured, string fallback, string name)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        return ValidBounded(value, 128) ? value : throw new InvalidOperationException($"{SectionPath}:{name} is invalid.");
    }

    private static string[] ReadValues(IConfigurationSection section, string? scalar) =>
        section.GetChildren().Select(static child => child.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Concat((scalar ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool TryUniqueClaim(ClaimsIdentity identity, string claimType, out string value)
    {
        var values = identity.FindAll(claimType).Select(static claim => claim.Value).ToArray();
        value = values.Length == 1 ? values[0] : string.Empty;
        return values.Length == 1 && string.Equals(value, value.Trim(), StringComparison.Ordinal) && ValidBounded(value, 256);
    }

    private static bool ValidBounded(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);
}

public enum UiExternalAuthenticationStatus { NotPresented, Authenticated, Rejected }

public sealed record UiExternalAuthenticationResult(UiExternalAuthenticationStatus Status, RuntimeRequestContext? Context = null);

public sealed class UiExternalIdentityAuthenticator(UiExternalIdentityOptions options)
{
    private const int MaximumAuthorizationHeaderLength = 8 * 1024;

    public async Task<UiExternalAuthenticationResult> AuthenticateAsync(ServerCallContext callContext)
        => await AuthenticateAsync(callContext.GetHttpContext(), callContext.CancellationToken).ConfigureAwait(false);

    public async Task<UiExternalAuthenticationResult> AuthenticateAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return new(UiExternalAuthenticationStatus.NotPresented);
        var authorization = httpContext.Request.Headers.Authorization;
        if (authorization.Count == 0) return new(UiExternalAuthenticationStatus.NotPresented);
        if (authorization.Count != 1 || authorization[0] is not { } header || header.Length is 0 or > MaximumAuthorizationHeaderLength ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header["Bearer ".Length..]))
            return new(UiExternalAuthenticationStatus.Rejected);

        try
        {
            var result = await httpContext.AuthenticateAsync(UiExternalIdentityOptions.AuthenticationScheme).ConfigureAwait(false);
            if (!result.Succeeded || result.Principal is null || !options.TryMapPrincipal(result.Principal, out var mapped))
                return new(UiExternalAuthenticationStatus.Rejected);
            return new(UiExternalAuthenticationStatus.Authenticated, mapped);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(UiExternalAuthenticationStatus.Rejected);
        }
    }
}
