using System.Xml.Linq;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;
using Brain.Product.Abstractions.Activities;
using Brain.Product.Abstractions.Authority;
using Brain.Product.Abstractions.Operations;
using Xunit;

namespace Brain.ProductHost.Tests;

public sealed class ProductBoundaryContractTests
{
    private static readonly WorkspaceId Workspace = new("workspace-1");
    private static readonly PrincipalId Principal = new("principal-1");
    private static readonly DateTimeOffset IssuedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void Grant_rejects_expired_or_empty_security_identity()
    {
        Assert.Throws<ArgumentException>(() =>
            BrainAccessGrant.Create(
                default,
                new PrincipalId("p"),
                [],
                [],
                [],
                1,
                IssuedAt,
                IssuedAt.AddMinutes(1),
                IssuedAt));
        Assert.Throws<ArgumentException>(() =>
            BrainAccessGrant.Create(
                Workspace,
                default,
                [],
                [],
                [],
                1,
                IssuedAt,
                IssuedAt.AddMinutes(1),
                IssuedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrainAccessGrant.Create(
                Workspace,
                Principal,
                [],
                [],
                [],
                0,
                IssuedAt,
                IssuedAt.AddMinutes(1),
                IssuedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrainAccessGrant.Create(
                Workspace,
                Principal,
                [],
                [],
                [],
                1,
                IssuedAt.AddMinutes(-5),
                IssuedAt.AddTicks(-1),
                IssuedAt));
    }

    [Fact]
    public void Grant_enforces_exact_fifteen_minute_maximum_lifetime()
    {
        var exactMaximum = BrainAccessGrant.Create(
            Workspace,
            Principal,
            [],
            [],
            [],
            1,
            IssuedAt,
            IssuedAt.AddMinutes(15),
            IssuedAt);

        Assert.Equal(IssuedAt, exactMaximum.IssuedAt);
        Assert.Equal(IssuedAt.AddMinutes(15), exactMaximum.ExpiresAt);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrainAccessGrant.Create(
                Workspace,
                Principal,
                [],
                [],
                [],
                1,
                IssuedAt,
                IssuedAt.AddMinutes(15).AddTicks(1),
                IssuedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrainAccessGrant.Create(
                Workspace,
                Principal,
                [],
                [],
                [],
                1,
                IssuedAt,
                IssuedAt,
                IssuedAt.AddMinutes(-1)));
    }

    [Fact]
    public void Grant_owns_immutable_copies_of_authority_claims()
    {
        var roles = new List<string> { "operator" };
        var grants = new List<string> { "operations.invoke" };
        var connections = new List<ConnectionReference> { new("calendar-primary") };

        var grant = BrainAccessGrant.Create(
            Workspace,
            Principal,
            roles,
            grants,
            connections,
            7,
            IssuedAt,
            IssuedAt.AddMinutes(5),
            IssuedAt);

        roles[0] = "mutated";
        grants.Clear();
        connections.Add(new ConnectionReference("mail-primary"));

        Assert.Equal(["operator"], grant.Roles);
        Assert.Equal(["operations.invoke"], grant.Grants);
        Assert.Equal([new ConnectionReference("calendar-primary")], grant.Connections);
        Assert.Equal(7, grant.PolicyVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Connection_reference_requires_an_opaque_non_empty_value(string value)
    {
        Assert.Throws<ArgumentException>(() => new ConnectionReference(value));
    }

    [Fact]
    public async Task Authentication_request_contains_only_authentication_evidence()
    {
        IBrainAccessAuthority authority = new FixtureAuthority(Workspace, Principal);
        var evidence = new AuthorityAuthenticationEvidence("fixture", "opaque-evidence");
        var request = new AuthorityAuthenticationRequest(evidence);

        var grant = await authority.AuthenticateAsync(request, TestContext.Current.CancellationToken);
        var presentations = await authority.GetWorkspacePresentationsAsync(
            grant,
            TestContext.Current.CancellationToken);

        Assert.Equal(Workspace, grant.Workspace);
        Assert.Equal(Principal, grant.Principal);
        Assert.Equal([new WorkspacePresentation(Workspace, "Research workspace")], presentations);
        var requestProperties = typeof(AuthorityAuthenticationRequest).GetProperties();
        Assert.Single(requestProperties);
        Assert.Equal(nameof(AuthorityAuthenticationRequest.Evidence), requestProperties[0].Name);
        Assert.Equal(typeof(AuthorityAuthenticationEvidence), requestProperties[0].PropertyType);
        Assert.DoesNotContain(requestProperties, static property => property.PropertyType == typeof(string));
        var authenticate = typeof(IBrainAccessAuthority).GetMethod(nameof(IBrainAccessAuthority.AuthenticateAsync));
        Assert.Equal(
            [typeof(AuthorityAuthenticationRequest), typeof(CancellationToken)],
            authenticate!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void Authentication_evidence_and_request_redact_credential_from_text()
    {
        const string literalCredential = "literal-secret-credential";
        var evidence = new AuthorityAuthenticationEvidence("fixture", literalCredential);
        var request = new AuthorityAuthenticationRequest(evidence);

        Assert.DoesNotContain(literalCredential, evidence.ToString());
        Assert.DoesNotContain(literalCredential, request.ToString());
        Assert.Contains("[REDACTED]", evidence.ToString());
        Assert.Contains("[REDACTED]", request.ToString());
    }

    [Fact]
    public void Product_contracts_project_core_descriptors_without_exposing_product_details_to_core()
    {
        var operation = new OperationDescriptor(
            new OperationId("proof.run"),
            new ContractId("proof/input@1"),
            new ContractId("proof/result@1"),
            new NeuronRoleId("proof.entry"),
            new ModuleId("proof"),
            new ContractVersion(1));
        var descriptor = new ProductOperationDescriptor(
            operation,
            "Run proof",
            "{\"type\":\"object\"}",
            "{\"type\":\"object\"}");
        var presentation = new WorkspacePresentation(Workspace, "Research workspace");
        var grant = BrainAccessGrant.Create(
            Workspace,
            Principal,
            ["operator"],
            ["operations.invoke"],
            [new ConnectionReference("calendar-primary")],
            3,
            IssuedAt,
            IssuedAt.AddMinutes(5),
            IssuedAt);
        var context = new ProductInvocationContext(grant, new IdempotencyKey("request-1"));
        var activity = BrainActivityId.New();
        var receipt = new ProductActivityReceipt(activity, operation.Id);
        var projection = new ProductActivityProjection(
            ActivityView.Accepted(activity, operation.Id, operation.TerminalResultContract),
            null,
            null);

        Assert.Equal("Run proof", descriptor.DisplayName);
        Assert.Equal("Research workspace", presentation.DisplayName);
        Assert.Equal(Workspace, context.AccessGrant.Workspace);
        Assert.Equal(operation.Id, receipt.Operation);
        Assert.Equal(ActivityStatus.Accepted, projection.Activity.Status);
    }

    [Fact]
    public void Core_projects_do_not_reference_product_abstractions()
    {
        AssertProjectHasNoReference("src/CoreV2/Brain.Abstractions/Brain.Abstractions.csproj", "Brain.Product.Abstractions");
        AssertProjectHasNoReference("src/CoreV2/Brain.Core/Brain.Core.csproj", "Brain.Product.Abstractions");

        var root = RepositoryRoot();
        var moduleContracts = Directory.GetFiles(
            Path.Combine(root, "src", "CoreV2", "Modules"),
            "*.csproj",
            SearchOption.AllDirectories)
            .Where(static path => path.Replace('\\', '/').Contains(".Contracts/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(moduleContracts);
        Assert.All(moduleContracts, project => AssertProjectHasNoReference(project, "Brain.Product.Abstractions"));
    }

    private static void AssertProjectHasNoReference(string projectPath, string forbiddenProjectName)
    {
        var root = RepositoryRoot();
        var absolutePath = Path.IsPathRooted(projectPath) ? projectPath : Path.Combine(root, projectPath);
        var document = XDocument.Load(absolutePath);
        var references = document
            .Descendants("ProjectReference")
            .Select(static reference => (string?)reference.Attribute("Include"))
            .Where(static include => include is not null);

        Assert.DoesNotContain(
            references,
            reference => reference!.Contains(forbiddenProjectName, StringComparison.OrdinalIgnoreCase));
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

    private sealed class FixtureAuthority(WorkspaceId workspace, PrincipalId principal) : IBrainAccessAuthority
    {
        public Task<BrainAccessGrant> AuthenticateAsync(
            AuthorityAuthenticationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BrainAccessGrant.Create(
                workspace,
                principal,
                ["operator"],
                ["operations.invoke"],
                [],
                1,
                IssuedAt,
                IssuedAt.AddMinutes(1),
                IssuedAt));
        }

        public Task<IReadOnlyList<WorkspacePresentation>> GetWorkspacePresentationsAsync(
            BrainAccessGrant accessGrant,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(accessGrant);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<WorkspacePresentation> presentations =
                [new WorkspacePresentation(accessGrant.Workspace, "Research workspace")];
            return Task.FromResult(presentations);
        }
    }
}
