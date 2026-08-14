using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Brain.Abstractions.Identity;
using Brain.Product.Abstractions.Authority;
using DigitalBrain.ProductHost.Authority;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Brain.Authority.Conformance.Tests;

public abstract class AuthorityConformanceTests
{
    protected abstract IBrainAccessAuthority Authority { get; }

    protected abstract AuthorityAuthenticationRequest Issue(AuthorityFixture fixture);

    [Fact]
    public async Task Rejects_wrong_audience_expired_and_missing_workspace_grants()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.WrongAudience()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.Expired()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.WithoutWorkspace()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_expected_audience_accompanied_by_another_audience()
        => await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.AdditionalAudience()), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Rejects_wrong_issuer_and_signing_key()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.WrongIssuer()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.WrongSigningKey()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_overlong_or_future_issued_grants()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.Overlong()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.FutureIssued()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Maps_subject_workspace_roles_grants_connections_and_policy_version()
    {
        var grant = await Authority.AuthenticateAsync(
            Issue(AuthorityFixture.Member()),
            TestContext.Current.CancellationToken);

        Assert.Equal(new WorkspaceId("workspace-a"), grant.Workspace);
        Assert.Equal(new PrincipalId("principal-a"), grant.Principal);
        Assert.Equal(BrainPrincipalKind.Human, grant.PrincipalKind);
        Assert.Equal(["member"], grant.Roles);
        Assert.Equal(["connection_use"], grant.Grants);
        Assert.Equal([new ConnectionReference("conn-a")], grant.Connections);
        Assert.Equal(7, grant.PolicyVersion);
        Assert.Equal(AuthorityFixture.Now, grant.IssuedAt);
        Assert.Equal(AuthorityFixture.Now.AddMinutes(5), grant.ExpiresAt);
    }

    [Fact]
    public async Task Maps_signed_closed_service_principal_kind_and_rejects_unknown_kind()
    {
        var service = await Authority.AuthenticateAsync(
            Issue(AuthorityFixture.Member() with { PrincipalKind = BrainPrincipalKind.Service }),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrainPrincipalKind.Service, service.PrincipalKind);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Authority.AuthenticateAsync(
            Issue(AuthorityFixture.InvalidPrincipalKind()),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_duplicate_or_empty_closed_schema_claim_values()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.DuplicateRole()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.EmptyGrant()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.DuplicateWorkspace()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_unapproved_claim_value_shapes_before_grant_creation()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.NumericWorkspace()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.BooleanRole()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.StringPolicyVersion()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Authority.AuthenticateAsync(Issue(AuthorityFixture.StringIssuedAt()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Presentation_is_non_authorizing_and_scoped_to_authenticated_workspace()
    {
        var request = Issue(AuthorityFixture.Member());
        var grant = await Authority.AuthenticateAsync(request, TestContext.Current.CancellationToken);
        var presentation = await Authority.GetWorkspacePresentationsAsync(grant, TestContext.Current.CancellationToken);

        Assert.Equal([new Brain.Product.Abstractions.Operations.WorkspacePresentation(new WorkspaceId("workspace-a"), "workspace-a")], presentation);
        var authenticatedAgain = await Authority.AuthenticateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(grant.Workspace, authenticatedAgain.Workspace);
        Assert.Equal(grant.Principal, authenticatedAgain.Principal);
        Assert.Equal(grant.Roles, authenticatedAgain.Roles);
        Assert.Equal(grant.Grants, authenticatedAgain.Grants);
        Assert.Equal(grant.Connections, authenticatedAgain.Connections);
        Assert.Equal(grant.PolicyVersion, authenticatedAgain.PolicyVersion);
    }
}

public sealed class OidcClaimsAuthorityConformanceTests : AuthorityConformanceTests
{
    private readonly ConformanceTokenIssuer _fixture = new();

    protected override IBrainAccessAuthority Authority => _fixture.Authority;

    protected override AuthorityAuthenticationRequest Issue(AuthorityFixture fixture)
        => _fixture.Issue(fixture);
}

public sealed class AuthorityBoundaryTests
{
    private readonly ConformanceTokenIssuer _fixture = new();

    [Fact]
    public void Fixture_authority_is_internal_in_debug_and_absent_from_release_product_host()
    {
        var fixtureType = typeof(OidcClaimsAuthority).Assembly.GetType(
            "DigitalBrain.ProductHost.Authority.LocalTestAuthority",
            throwOnError: false,
            ignoreCase: false);

#if DEBUG
        Assert.NotNull(fixtureType);
        Assert.False(fixtureType.IsVisible);
#else
        Assert.Null(fixtureType);
        var releaseAssemblyImage = Encoding.Latin1.GetString(File.ReadAllBytes(typeof(OidcClaimsAuthority).Assembly.Location));
        Assert.DoesNotContain("https://local-authority.digitalbrain.test", releaseAssemblyImage, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "digitalbrain-local-authority-test-key-v1-only-not-a-secret",
            releaseAssemblyImage,
            StringComparison.Ordinal);
#endif
    }

#if DEBUG
    [Fact]
    public async Task Debug_local_fixture_remains_usable_by_the_conformance_assembly()
    {
        var authority = new LocalTestAuthority(new FrozenTimeProvider(AuthorityFixture.Now));
        var request = authority.Issue(
            "workspace-a",
            "principal-a",
            ["member"],
            ["connection_use"],
            ["conn-a"],
            BrainPrincipalKind.Human,
            7,
            AuthorityFixture.Now,
            AuthorityFixture.Now.AddMinutes(5));

        var grant = await authority.AuthenticateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(new WorkspaceId("workspace-a"), grant.Workspace);
        Assert.Equal(new PrincipalId("principal-a"), grant.Principal);
    }
#endif

    [Fact]
    public void Configurable_claim_names_reject_reserved_protocol_collisions()
    {
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            SubjectClaim = "iat",
        }));
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            WorkspaceClaim = "iss",
        }));
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            WorkspaceClaim = "sub",
        }));
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            PrincipalKindClaim = "client_id",
        }));
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            PolicyVersionClaim = "jti",
        }));
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            RoleClaim = "email",
        }));
        Assert.Throws<ArgumentException>(() => CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            GrantClaim = "auth_time",
        }));

        _ = CreateAuthority(new AuthorityOptions(ConformanceTokenIssuer.Issuer, ConformanceTokenIssuer.Audience)
        {
            SubjectClaim = "sub",
        });
    }

    [Fact]
    public void Authentication_request_has_no_caller_selected_workspace_or_principal()
    {
        var properties = typeof(AuthorityAuthenticationRequest).GetProperties();

        Assert.Single(properties);
        Assert.Equal(nameof(AuthorityAuthenticationRequest.Evidence), properties[0].Name);
        Assert.Equal(typeof(AuthorityAuthenticationEvidence), properties[0].PropertyType);
    }

    [Fact]
    public async Task Rejection_does_not_disclose_opaque_credential()
    {
        const string credential = "literal-secret-credential";
        var request = new AuthorityAuthenticationRequest(new AuthorityAuthenticationEvidence("Bearer", credential));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _fixture.Authority.AuthenticateAsync(request, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(credential, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Product_host_is_an_open_source_orleans_client_process()
    {
        var root = RepositoryRoot();
        var productHostDirectory = Path.Combine(root, "src", "CoreV2", "DigitalBrain.ProductHost");
        var project = XDocument.Load(Path.Combine(productHostDirectory, "DigitalBrain.ProductHost.csproj"));
        var projectReferences = project.Descendants("ProjectReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => include is not null)
            .ToArray();
        var sourceFiles = Directory.GetFiles(productHostDirectory, "*.cs", SearchOption.AllDirectories);
        sourceFiles = sourceFiles
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal("Microsoft.NET.Sdk", (string?)project.Root?.Attribute("Sdk"));
        Assert.Contains(project.Descendants("OutputType"), static element => element.Value == "Exe");
        Assert.Equal(
            [
                "../Aspire/Brain.Aspire/Brain.Aspire.csproj",
                "../Brain.Product.Abstractions/Brain.Product.Abstractions.csproj",
                "../Brain.Core/Brain.Core.csproj",
            ],
            projectReferences);
        var program = Assert.Single(
            sourceFiles,
            static path => Path.GetFileName(path).Equals("Program.cs", StringComparison.OrdinalIgnoreCase));
        var programSource = File.ReadAllText(program);
        Assert.Contains("AddDigitalBrainClient", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDigitalBrainRuntime", programSource, StringComparison.Ordinal);
        Assert.All(sourceFiles, path => Assert.DoesNotContain("IntoChat", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate DigitalBrain.slnx.");
    }

    private static OidcClaimsAuthority CreateAuthority(AuthorityOptions options)
        => new(
            options,
            new TokenValidationParameters { IssuerSigningKey = ConformanceTokenIssuer.SigningKey },
            new FrozenTimeProvider(AuthorityFixture.Now));
}

public sealed class IntoChatAuthorityExample : AuthorityConformanceTests
{
    private readonly ConformanceTokenIssuer _externalFixtureIssuer = new();

    protected override IBrainAccessAuthority Authority => _externalFixtureIssuer.Authority;

    protected override AuthorityAuthenticationRequest Issue(AuthorityFixture fixture)
        => _externalFixtureIssuer.Issue(fixture);
}

public sealed record AuthorityFixture(
    string? Workspace,
    string Principal,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Grants,
    IReadOnlyList<string> Connections,
    int PolicyVersion,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    BrainPrincipalKind PrincipalKind = BrainPrincipalKind.Human,
    string? Audience = null,
    string? Issuer = null,
    bool UseUntrustedSigningKey = false,
    AuthorityFixtureShape Shape = AuthorityFixtureShape.Canonical)
{
    public static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static AuthorityFixture Member()
        => new("workspace-a", "principal-a", ["member"], ["connection_use"], ["conn-a"], 7, Now, Now.AddMinutes(5));

    public static AuthorityFixture WrongAudience() => Member() with { Audience = "wrong-audience" };

    public static AuthorityFixture AdditionalAudience()
        => Member() with { Shape = AuthorityFixtureShape.AdditionalAudience };

    public static AuthorityFixture WrongIssuer() => Member() with { Issuer = "https://untrusted-issuer.example" };

    public static AuthorityFixture WrongSigningKey() => Member() with { UseUntrustedSigningKey = true };

    public static AuthorityFixture Expired() => Member() with { IssuedAt = Now.AddMinutes(-10), ExpiresAt = Now.AddMinutes(-5) };

    public static AuthorityFixture Overlong() => Member() with { ExpiresAt = Now.AddMinutes(15).AddSeconds(1) };

    public static AuthorityFixture FutureIssued()
        => Member() with { IssuedAt = Now.AddSeconds(10), ExpiresAt = Now.AddMinutes(5) };

    public static AuthorityFixture WithoutWorkspace() => Member() with { Workspace = null };

    public static AuthorityFixture DuplicateRole() => Member() with { Roles = ["member", "member"] };

    public static AuthorityFixture EmptyGrant() => Member() with { Grants = ["connection_use", " "] };

    public static AuthorityFixture DuplicateWorkspace()
        => Member() with { Shape = AuthorityFixtureShape.DuplicateWorkspace };

    public static AuthorityFixture NumericWorkspace()
        => Member() with { Shape = AuthorityFixtureShape.NumericWorkspace };

    public static AuthorityFixture BooleanRole()
        => Member() with { Shape = AuthorityFixtureShape.BooleanRole };

    public static AuthorityFixture StringPolicyVersion()
        => Member() with { Shape = AuthorityFixtureShape.StringPolicyVersion };

    public static AuthorityFixture StringIssuedAt()
        => Member() with { Shape = AuthorityFixtureShape.StringIssuedAt };

    public static AuthorityFixture InvalidPrincipalKind()
        => Member() with { Shape = AuthorityFixtureShape.InvalidPrincipalKind };
}

public enum AuthorityFixtureShape
{
    Canonical,
    AdditionalAudience,
    DuplicateWorkspace,
    NumericWorkspace,
    BooleanRole,
    StringPolicyVersion,
    StringIssuedAt,
    InvalidPrincipalKind,
}

internal sealed class ConformanceTokenIssuer
{
    public const string Issuer = "https://authority-conformance.digitalbrain.test";
    public const string Audience = "digitalbrain-product";
    public static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("digitalbrain-authority-conformance-signing-key-v1-test-only"));
    private static readonly SigningCredentials SigningCredentials =
        new(SigningKey, SecurityAlgorithms.HmacSha256);
    private static readonly SigningCredentials UntrustedSigningCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes("digitalbrain-authority-untrusted-signing-key-v1-test-only")),
        SecurityAlgorithms.HmacSha256);
    private readonly JsonWebTokenHandler _tokenHandler = new() { MapInboundClaims = false };

    public ConformanceTokenIssuer()
    {
        Authority = new OidcClaimsAuthority(
            new AuthorityOptions(Issuer, Audience),
            new TokenValidationParameters { IssuerSigningKey = SigningKey },
            new FrozenTimeProvider(AuthorityFixture.Now));
    }

    public IBrainAccessAuthority Authority { get; }

    public AuthorityAuthenticationRequest Issue(AuthorityFixture fixture)
    {
        var issuedAt = fixture.IssuedAt.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = fixture.Issuer ?? Issuer,
            ["aud"] = fixture.Shape == AuthorityFixtureShape.AdditionalAudience
                ? new[] { fixture.Audience ?? Audience, "another-product" }
                : fixture.Audience ?? Audience,
            ["nbf"] = issuedAt,
            ["iat"] = fixture.Shape == AuthorityFixtureShape.StringIssuedAt
                ? issuedAt.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : issuedAt,
            ["exp"] = fixture.ExpiresAt.ToUnixTimeSeconds(),
            [AuthorityOptions.DefaultSubjectClaim] = fixture.Principal,
            [AuthorityOptions.DefaultPrincipalKindClaim] = fixture.Shape == AuthorityFixtureShape.InvalidPrincipalKind
                ? "robot"
                : fixture.PrincipalKind == BrainPrincipalKind.Human ? "human" : "service",
            [AuthorityOptions.DefaultPolicyVersionClaim] = fixture.Shape == AuthorityFixtureShape.StringPolicyVersion
                ? fixture.PolicyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : fixture.PolicyVersion,
        };

        if (fixture.Workspace is not null)
        {
            payload[AuthorityOptions.DefaultWorkspaceClaim] = fixture.Shape switch
            {
                AuthorityFixtureShape.DuplicateWorkspace => new[] { fixture.Workspace, "workspace-b" },
                AuthorityFixtureShape.NumericWorkspace => 42,
                _ => fixture.Workspace,
            };
        }

        AddStringList(payload, AuthorityOptions.DefaultRoleClaim, fixture.Roles,
            fixture.Shape == AuthorityFixtureShape.BooleanRole ? true : null);
        AddStringList(payload, AuthorityOptions.DefaultGrantClaim, fixture.Grants);
        AddStringList(payload, AuthorityOptions.DefaultConnectionClaim, fixture.Connections);

        var credentials = fixture.UseUntrustedSigningKey ? UntrustedSigningCredentials : SigningCredentials;
        var token = _tokenHandler.CreateToken(JsonSerializer.Serialize(payload), credentials);
        return new AuthorityAuthenticationRequest(new AuthorityAuthenticationEvidence("Bearer", token));
    }

    private static void AddStringList(
        IDictionary<string, object?> payload,
        string claimName,
        IReadOnlyList<string> values,
        object? overrideValue = null)
    {
        if (overrideValue is not null)
        {
            payload[claimName] = overrideValue;
        }
        else if (values.Count == 1)
        {
            payload[claimName] = values[0];
        }
        else if (values.Count > 1)
        {
            payload[claimName] = values;
        }
    }
}

internal sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
