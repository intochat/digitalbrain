using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
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
    private const string IssuerClaim = "iss";
    private const string AudienceClaim = "aud";
    private const string NotBeforeClaim = "nbf";
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
            ValidateRawClaimShapes(token);
            var claims = token.Claims.ToArray();
            var audiences = token.Audiences.ToArray();
            if (audiences.Length != 1
                || !string.Equals(audiences[0], _options.Audience, StringComparison.Ordinal)
                || !string.Equals(RequiredSingleString(claims, AudienceClaim), _options.Audience, StringComparison.Ordinal))
            {
                throw Unauthorized();
            }

            _ = RequiredSingleString(claims, IssuerClaim);
            EnsureOptionalNumericDate(claims, NotBeforeClaim);
            var workspace = new WorkspaceId(RequiredSingleString(claims, _options.WorkspaceClaim));
            var principal = new PrincipalId(RequiredSingleString(claims, _options.SubjectClaim));
            var roles = OptionalDistinctStrings(claims, _options.RoleClaim);
            var grants = OptionalDistinctStrings(claims, _options.GrantClaim);
            var connections = OptionalDistinctStrings(claims, _options.ConnectionClaim)
                .Select(static value => new ConnectionReference(value));
            var policyVersion = ParsePositiveInt(RequiredSingleValue(
                claims,
                _options.PolicyVersionClaim,
                ClaimValueTypes.Integer32));
            var issuedAt = ParseNumericDate(RequiredSingleValue(claims, IssuedAtClaim, ClaimValueTypes.Integer64));
            var expiresAt = ParseNumericDate(RequiredSingleValue(claims, ExpiresAtClaim, ClaimValueTypes.Integer64));
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

    private void ValidateRawClaimShapes(JsonWebToken token)
    {
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
        var root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Unauthorized();
        }

        RequireRawString(root, IssuerClaim);
        RequireRawSingleAudience(root);
        RequireRawString(root, _options.SubjectClaim);
        RequireRawString(root, _options.WorkspaceClaim);
        RequireRawStringList(root, _options.RoleClaim);
        RequireRawStringList(root, _options.GrantClaim);
        RequireRawStringList(root, _options.ConnectionClaim);
        RequireRawCanonicalInt32(root, _options.PolicyVersionClaim);
        RequireRawCanonicalNumericDate(root, IssuedAtClaim);
        RequireRawCanonicalNumericDate(root, ExpiresAtClaim);
        RequireRawCanonicalNumericDate(root, NotBeforeClaim, required: false);
    }

    private void RequireRawSingleAudience(JsonElement root)
    {
        var audience = RequireSingleRawProperty(root, AudienceClaim);
        var valid = audience.ValueKind switch
        {
            JsonValueKind.String => string.Equals(audience.GetString(), _options.Audience, StringComparison.Ordinal),
            JsonValueKind.Array => audience.GetArrayLength() == 1
                && audience[0].ValueKind == JsonValueKind.String
                && string.Equals(audience[0].GetString(), _options.Audience, StringComparison.Ordinal),
            _ => false,
        };
        if (!valid)
        {
            throw Unauthorized();
        }
    }

    private static void RequireRawString(JsonElement root, string claimType)
    {
        var value = RequireSingleRawProperty(root, claimType);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Unauthorized();
        }
    }

    private static void RequireRawStringList(JsonElement root, string claimType)
    {
        var properties = RawProperties(root, claimType);
        if (properties.Length == 0)
        {
            return;
        }

        if (properties.Length != 1)
        {
            throw Unauthorized();
        }

        var value = properties[0].Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            if (string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw Unauthorized();
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Array
            || value.EnumerateArray().Any(static item =>
                item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
        {
            throw Unauthorized();
        }
    }

    private static void RequireRawCanonicalInt32(JsonElement root, string claimType)
    {
        var value = RequireSingleRawProperty(root, claimType);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed)
            || !string.Equals(value.GetRawText(), parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw Unauthorized();
        }
    }

    private static void RequireRawCanonicalNumericDate(JsonElement root, string claimType, bool required = true)
    {
        var properties = RawProperties(root, claimType);
        if (!required && properties.Length == 0)
        {
            return;
        }

        if (properties.Length != 1
            || properties[0].Value.ValueKind != JsonValueKind.Number
            || !properties[0].Value.TryGetInt64(out var parsed)
            || !string.Equals(
                properties[0].Value.GetRawText(),
                parsed.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw Unauthorized();
        }
    }

    private static JsonElement RequireSingleRawProperty(JsonElement root, string claimType)
    {
        var properties = RawProperties(root, claimType);
        if (properties.Length != 1)
        {
            throw Unauthorized();
        }

        return properties[0].Value;
    }

    private static JsonProperty[] RawProperties(JsonElement root, string claimType)
        => root.EnumerateObject()
            .Where(property => string.Equals(property.Name, claimType, StringComparison.Ordinal))
            .ToArray();

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

    private static string RequiredSingleString(IEnumerable<Claim> claims, string claimType)
        => RequiredSingleValue(claims, claimType, ClaimValueTypes.String);

    private static string RequiredSingleValue(
        IEnumerable<Claim> claims,
        string claimType,
        string requiredValueType)
    {
        var matches = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1
            || !string.Equals(matches[0].ValueType, requiredValueType, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(matches[0].Value))
        {
            throw Unauthorized();
        }

        return matches[0].Value;
    }

    private static IReadOnlyList<string> OptionalDistinctStrings(IEnumerable<Claim> claims, string claimType)
    {
        var matches = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .ToArray();
        if (matches.Any(static claim => !string.Equals(claim.ValueType, ClaimValueTypes.String, StringComparison.Ordinal))
            || matches.Any(static claim => string.IsNullOrWhiteSpace(claim.Value)))
        {
            throw Unauthorized();
        }

        var values = matches.Select(static claim => claim.Value).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw Unauthorized();
        }

        return values;
    }

    private static void EnsureOptionalNumericDate(IEnumerable<Claim> claims, string claimType)
    {
        var matches = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        if (matches.Length != 1
            || !string.Equals(matches[0].ValueType, ClaimValueTypes.Integer64, StringComparison.Ordinal)
            || !long.TryParse(matches[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw Unauthorized();
        }
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
