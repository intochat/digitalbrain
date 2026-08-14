using System.Security.Claims;
using System.Xml.Linq;
using Brain.Abstractions.Identity;
using Brain.Product.Abstractions.Authority;
using DigitalBrain.ProductHost.Authority;
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
        Assert.Equal(["member"], grant.Roles);
        Assert.Equal(["connection_use"], grant.Grants);
        Assert.Equal([new ConnectionReference("conn-a")], grant.Connections);
        Assert.Equal(7, grant.PolicyVersion);
        Assert.Equal(AuthorityFixture.Now, grant.IssuedAt);
        Assert.Equal(AuthorityFixture.Now.AddMinutes(5), grant.ExpiresAt);
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

public sealed class LocalTestAuthorityConformanceTests : AuthorityConformanceTests
{
    private readonly LocalTestAuthority _authority = new("Development", new FrozenTimeProvider(AuthorityFixture.Now));

    protected override IBrainAccessAuthority Authority => _authority;

    protected override AuthorityAuthenticationRequest Issue(AuthorityFixture fixture)
        => _authority.Issue(
            fixture.Workspace,
            fixture.Principal,
            fixture.Roles,
            fixture.Grants,
            fixture.Connections,
            fixture.PolicyVersion,
            fixture.IssuedAt,
            fixture.ExpiresAt,
            fixture.Audience,
            fixture.AdditionalClaims,
            fixture.Issuer,
            fixture.UseUntrustedSigningKey);

    [Fact]
    public void Local_authority_rejects_production_selection()
        => Assert.Throws<InvalidOperationException>(() => new LocalTestAuthority("Production"));

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
            Authority.AuthenticateAsync(request, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(credential, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Product_host_is_a_library_only_open_source_adapter_container()
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
        Assert.DoesNotContain(project.Descendants("OutputType"), static element => element.Value == "Exe");
        Assert.Equal(["../Brain.Product.Abstractions/Brain.Product.Abstractions.csproj"], projectReferences);
        Assert.DoesNotContain(sourceFiles, static path => Path.GetFileName(path).Equals("Program.cs", StringComparison.OrdinalIgnoreCase));
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
}

public sealed class IntoChatAuthorityExample : AuthorityConformanceTests
{
    private readonly LocalTestAuthority _externalFixtureIssuer = new("Test", new FrozenTimeProvider(AuthorityFixture.Now));

    protected override IBrainAccessAuthority Authority => _externalFixtureIssuer;

    protected override AuthorityAuthenticationRequest Issue(AuthorityFixture fixture)
        => _externalFixtureIssuer.Issue(
            fixture.Workspace,
            fixture.Principal,
            fixture.Roles,
            fixture.Grants,
            fixture.Connections,
            fixture.PolicyVersion,
            fixture.IssuedAt,
            fixture.ExpiresAt,
            fixture.Audience,
            fixture.AdditionalClaims,
            fixture.Issuer,
            fixture.UseUntrustedSigningKey);
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
    string? Audience = null,
    IReadOnlyList<Claim>? AdditionalClaims = null,
    string? Issuer = null,
    bool UseUntrustedSigningKey = false)
{
    public static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static AuthorityFixture Member()
        => new("workspace-a", "principal-a", ["member"], ["connection_use"], ["conn-a"], 7, Now, Now.AddMinutes(5));

    public static AuthorityFixture WrongAudience() => Member() with { Audience = "wrong-audience" };

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
        => Member() with { AdditionalClaims = [new Claim(AuthorityOptions.DefaultWorkspaceClaim, "workspace-b")] };
}

internal sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
