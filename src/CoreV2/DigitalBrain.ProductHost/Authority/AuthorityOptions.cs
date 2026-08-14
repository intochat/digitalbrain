namespace DigitalBrain.ProductHost.Authority;

public sealed class AuthorityOptions
{
    private static readonly HashSet<string> ReservedProtocolClaims = new(StringComparer.Ordinal)
    {
        "iss", "sub", "aud", "exp", "nbf", "iat", "jti",
        "auth_time", "nonce", "acr", "amr", "azp", "at_hash", "c_hash", "s_hash", "sid",
        "name", "given_name", "family_name", "middle_name", "nickname", "preferred_username",
        "profile", "picture", "website", "email", "email_verified", "gender", "birthdate",
        "zoneinfo", "locale", "phone_number", "phone_number_verified", "address", "updated_at",
        "client_id", "scope", "cnf", "act", "may_act", "events",
    };
    public const string DefaultSubjectClaim = "sub";
    public const string DefaultWorkspaceClaim = "brain_workspace";
    public const string DefaultRoleClaim = "brain_role";
    public const string DefaultGrantClaim = "brain_grant";
    public const string DefaultConnectionClaim = "brain_connection";
    public const string DefaultPolicyVersionClaim = "brain_policy_version";

    public AuthorityOptions(string issuer, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        Issuer = issuer;
        Audience = audience;
    }

    public string Issuer { get; }

    public string Audience { get; }

    public string AuthenticationScheme { get; init; } = "Bearer";

    public string SubjectClaim { get; init; } = DefaultSubjectClaim;

    public string WorkspaceClaim { get; init; } = DefaultWorkspaceClaim;

    public string RoleClaim { get; init; } = DefaultRoleClaim;

    public string GrantClaim { get; init; } = DefaultGrantClaim;

    public string ConnectionClaim { get; init; } = DefaultConnectionClaim;

    public string PolicyVersionClaim { get; init; } = DefaultPolicyVersionClaim;

    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        var claimNames = new[]
        {
            SubjectClaim,
            WorkspaceClaim,
            RoleClaim,
            GrantClaim,
            ConnectionClaim,
            PolicyVersionClaim,
        };

        if (string.IsNullOrWhiteSpace(AuthenticationScheme) || claimNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Authority scheme and claim names cannot be empty.");
        }

        if (claimNames.Distinct(StringComparer.Ordinal).Count() != claimNames.Length)
        {
            throw new ArgumentException("Authority claim names must be distinct.");
        }

        if (ReservedProtocolClaims.Contains(SubjectClaim)
            && !string.Equals(SubjectClaim, DefaultSubjectClaim, StringComparison.Ordinal))
        {
            throw new ArgumentException("The subject mapping cannot use a reserved JWT or OIDC protocol claim.", nameof(SubjectClaim));
        }

        var authorizationClaims = claimNames.Skip(1);
        if (authorizationClaims.Any(ReservedProtocolClaims.Contains))
        {
            throw new ArgumentException("Authorization mappings cannot use reserved JWT or OIDC protocol claims.");
        }

        if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(ClockSkew), "Authority clock skew must be between zero and five minutes.");
        }
    }
}
