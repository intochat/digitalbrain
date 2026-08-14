using System.Globalization;
using System.Security.Claims;
using Brain.Abstractions.Identity;
using Brain.Product.Abstractions.Authority;
using Brain.Product.Abstractions.Operations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DigitalBrain.ProductHost.Authority;

public sealed class OidcClaimsAuthority : IBrainAccessAuthority
{
    private const string IssuedAtClaim = "iat";
    private const string ExpiresAtClaim = "exp";
    private readonly AuthorityOptions _options;
    private readonly JsonWebTokenHandler _tokenHandler;
    private readonly TimeProvider _timeProvider;
    private readonly TokenValidationParameters _validationParameters;

    public OidcClaimsAuthority(
        AuthorityOptions options,
        TokenValidationParameters validationParameters,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validationParameters);
        options.Validate();

        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tokenHandler = new JsonWebTokenHandler { MapInboundClaims = false };
        _validationParameters = SecureValidationParameters(validationParameters);
    }

    public async Task<BrainAccessGrant> AuthenticateAsync(
        AuthorityAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(request.Evidence.Scheme, _options.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw Unauthorized();
        }

        TokenValidationResult validation;
        try
        {
            validation = await _tokenHandler
                .ValidateTokenAsync(request.Evidence.OpaqueCredential, _validationParameters)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Unauthorized();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token)
        {
            throw Unauthorized();
        }

        try
        {
            var claims = token.Claims.ToArray();
            var workspace = new WorkspaceId(RequiredSingle(claims, _options.WorkspaceClaim));
            var principal = new PrincipalId(RequiredSingle(claims, _options.SubjectClaim));
            var roles = OptionalDistinct(claims, _options.RoleClaim);
            var grants = OptionalDistinct(claims, _options.GrantClaim);
            var connections = OptionalDistinct(claims, _options.ConnectionClaim)
                .Select(static value => new ConnectionReference(value));
            var policyVersion = ParsePositiveInt(RequiredSingle(claims, _options.PolicyVersionClaim));
            var issuedAt = ParseNumericDate(RequiredSingle(claims, IssuedAtClaim));
            var expiresAt = ParseNumericDate(RequiredSingle(claims, ExpiresAtClaim));
            var evaluatedAt = _timeProvider.GetUtcNow();

            return BrainAccessGrant.Create(
                workspace,
                principal,
                roles,
                grants,
                connections,
                policyVersion,
                issuedAt,
                expiresAt,
                evaluatedAt);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw Unauthorized();
        }
    }

    public Task<IReadOnlyList<WorkspacePresentation>> GetWorkspacePresentationsAsync(
        BrainAccessGrant accessGrant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessGrant);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorkspacePresentation> presentations =
            [new WorkspacePresentation(accessGrant.Workspace, accessGrant.Workspace.Value)];
        return Task.FromResult(presentations);
    }

    private TokenValidationParameters SecureValidationParameters(TokenValidationParameters supplied)
    {
        var secured = supplied.Clone();
        secured.ValidIssuer = _options.Issuer;
        secured.ValidIssuers = null;
        secured.ValidateIssuer = true;
        secured.IssuerValidator = null;
        secured.IssuerValidatorUsingConfiguration = null;
        secured.ValidAudience = _options.Audience;
        secured.ValidAudiences = null;
        secured.RequireAudience = true;
        secured.ValidateAudience = true;
        secured.AudienceValidator = null;
        secured.RequireSignedTokens = true;
        secured.ValidateIssuerSigningKey = true;
        secured.IssuerSigningKeyValidator = null;
        secured.IssuerSigningKeyValidatorUsingConfiguration = null;
        secured.RequireExpirationTime = true;
        secured.ValidateLifetime = true;
        secured.ClockSkew = _options.ClockSkew;
        secured.IncludeTokenOnFailedValidation = false;
        secured.LogTokenId = false;
        secured.SaveSigninToken = false;
        secured.SignatureValidator = null;
        secured.SignatureValidatorUsingConfiguration = null;
        secured.TokenReader = null;
        secured.TransformBeforeSignatureValidation = null;
        secured.LifetimeValidator = ValidateLifetime;
        return secured;
    }

    private bool ValidateLifetime(
        DateTime? notBefore,
        DateTime? expires,
        SecurityToken _,
        TokenValidationParameters validationParameters)
    {
        if (expires is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return (notBefore is null || notBefore.Value < expires.Value)
            && (notBefore is null || notBefore.Value <= now + validationParameters.ClockSkew)
            && expires.Value > now - validationParameters.ClockSkew;
    }

    private static string RequiredSingle(IEnumerable<Claim> claims, string claimType)
    {
        var values = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .Select(static claim => claim.Value)
            .ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw Unauthorized();
        }

        return values[0];
    }

    private static IReadOnlyList<string> OptionalDistinct(IEnumerable<Claim> claims, string claimType)
    {
        var values = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .Select(static claim => claim.Value)
            .ToArray();
        if (values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw Unauthorized();
        }

        return values;
    }

    private static int ParsePositiveInt(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw Unauthorized();
        }

        return parsed;
    }

    private static DateTimeOffset ParseNumericDate(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            throw Unauthorized();
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static UnauthorizedAccessException Unauthorized()
        => new("Authority authentication evidence was rejected.");
}
