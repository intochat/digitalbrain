using System.Text.Json;
using System.Text.Json.Serialization;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Modules;
using Brain.Product.Abstractions.Activities;
using Brain.Product.Abstractions.Authority;
using Brain.Product.Abstractions.Operations;
using DigitalBrain.ProductHost.Catalog;
using Xunit;

namespace Brain.ProductHost.Tests;

public sealed class ProductOperationCatalogTests
{
    private static readonly WorkspaceId Workspace = new("workspace-a");
    private static readonly PrincipalId Principal = new("principal-a");
    private static readonly DateTimeOffset IssuedAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ModuleId Module = new("conversation");
    private static readonly NeuronRoleId EntryRole = new("conversation.entry");
    private static readonly OperationDescriptor Send = Operation("conversation/send-message@1");
    private static readonly OperationDescriptor Hidden = Operation("conversation/read-diagnostics@1");
    private static readonly OperationDescriptor Unregistered = Operation("conversation/unregistered@1");

    [Fact]
    public async Task Discovery_intersects_manifest_registration_and_current_policy_deterministically()
    {
        var sendAdapter = Adapter(Send);
        var hiddenAdapter = Adapter(Hidden);
        var catalog = Catalog(
            [Send, Hidden, Unregistered],
            [Registration(Send, sendAdapter), Registration(Hidden, hiddenAdapter)],
            allowed: [Send.Id]);

        var visible = await catalog.DiscoverAsync(Grant(), TestContext.Current.CancellationToken);

        var descriptor = Assert.Single(visible);
        Assert.Equal(Send.Id, descriptor.Operation.Id);
        Assert.DoesNotContain(visible, candidate => candidate.Operation.Id == Hidden.Id);
        Assert.DoesNotContain(visible, candidate => candidate.Operation.Id == Unregistered.Id);
    }

    [Fact]
    public async Task Discovery_orders_explicit_registrations_by_canonical_operation_id()
    {
        var alpha = Operation("conversation/alpha@1");
        var zulu = Operation("conversation/zulu@1");
        var catalog = Catalog(
            [zulu, alpha],
            [Registration(zulu, Adapter(zulu)), Registration(alpha, Adapter(alpha))],
            allowed: [zulu.Id, alpha.Id]);

        var visible = await catalog.DiscoverAsync(Grant(), TestContext.Current.CancellationToken);

        Assert.Equal([alpha.Id, zulu.Id], visible.Select(static descriptor => descriptor.Operation.Id));
    }

    [Fact]
    public async Task Invocation_refuses_a_registered_operation_hidden_by_policy_before_adapter_use()
    {
        var adapter = Adapter(Hidden);
        var catalog = Catalog(
            [Hidden],
            [Registration(Hidden, adapter)],
            allowed: []);

        await Assert.ThrowsAsync<ProductOperationNotAvailableException>(() => catalog.InvokeAsync(
            Hidden.Id,
            Json("{\"message\":\"hello\"}"),
            Invocation(Grant()),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, adapter.InvocationCount);
    }

    [Fact]
    public async Task Discovery_and_invocation_require_the_registration_roles_and_grants()
    {
        var adapter = Adapter(Send);
        var registration = new ProductOperationRegistration(
            Send,
            adapter,
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            new ProductOperationAccessPolicy(["member"], ["operations.invoke"]));
        var catalog = Catalog([Send], [registration], allowed: [Send.Id]);
        var incompleteGrants = new[]
        {
            Grant(roles: [], grants: ["operations.invoke"]),
            Grant(roles: ["member"], grants: []),
        };

        foreach (var incompleteGrant in incompleteGrants)
        {
            Assert.Empty(await catalog.DiscoverAsync(incompleteGrant, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ProductOperationNotAvailableException>(() => catalog.InvokeAsync(
                Send.Id,
                Json("{\"message\":\"hello\"}"),
                Invocation(incompleteGrant),
                TestContext.Current.CancellationToken));
        }

        Assert.Equal(0, adapter.InvocationCount);
    }

    [Theory]
    [InlineData(6, "workspace-a")]
    [InlineData(7, "workspace-b")]
    public async Task Stale_policy_or_unknown_workspace_refuses_discovery_and_invocation(
        int grantPolicyVersion,
        string workspace)
    {
        var adapter = Adapter(Send);
        var catalog = Catalog(
            [Send],
            [Registration(Send, adapter)],
            allowed: [Send.Id]);
        var grant = Grant(new WorkspaceId(workspace), grantPolicyVersion);

        Assert.Empty(await catalog.DiscoverAsync(grant, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ProductOperationNotAvailableException>(() => catalog.InvokeAsync(
            Send.Id,
            Json("{\"message\":\"hello\"}"),
            Invocation(grant),
            TestContext.Current.CancellationToken));
        Assert.Equal(0, adapter.InvocationCount);
    }

    [Fact]
    public void Duplicate_operation_registration_is_rejected()
    {
        var registry = Registry([Send]);
        var filter = Filter([Send.Id]);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() => new ProductOperationCatalog(
            registry,
            filter,
            [Registration(Send, Adapter(Send)), Registration(Send, Adapter(Send))]));
    }

    [Fact]
    public void Manifest_and_adapter_descriptor_mismatch_is_rejected()
    {
        var mismatched = new OperationDescriptor(
            Send.Id,
            Send.InputContract,
            Send.TerminalResultContract,
            Send.EntryRole,
            Send.Owner,
            new ContractVersion(2));

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            Registration(Send, Adapter(mismatched)));
    }

    [Fact]
    public void Registration_for_an_uninstalled_module_is_rejected()
    {
        var uninstalled = new OperationDescriptor(
            new OperationId("external/run@1"),
            new ContractId("external/input@1"),
            new ContractId("external/result@1"),
            new NeuronRoleId("external.entry"),
            new ModuleId("external"),
            new ContractVersion(1));
        var registry = Registry([Send]);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() => new ProductOperationCatalog(
            registry,
            Filter([Send.Id, uninstalled.Id]),
            [Registration(uninstalled, Adapter(uninstalled))]));
    }

    [Theory]
    [InlineData("{\"message\":\"hello\",\"workspaceId\":\"workspace-b\"}")]
    [InlineData("{\"message\":42}")]
    [InlineData("[]")]
    public async Task Invalid_or_unknown_json_is_rejected_before_adapter_use(string input)
    {
        var adapter = Adapter(Send);
        var catalog = Catalog(
            [Send],
            [Registration(Send, adapter)],
            allowed: [Send.Id]);

        await Assert.ThrowsAsync<ProductOperationInputException>(() => catalog.InvokeAsync(
            Send.Id,
            Json(input),
            Invocation(Grant()),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, adapter.InvocationCount);
    }

    [Fact]
    public async Task Undefined_json_is_rejected_before_adapter_use()
    {
        var adapter = Adapter(Send);
        var catalog = Catalog(
            [Send],
            [Registration(Send, adapter)],
            allowed: [Send.Id]);

        await Assert.ThrowsAsync<ProductOperationInputException>(() => catalog.InvokeAsync(
            Send.Id,
            default,
            Invocation(Grant()),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, adapter.InvocationCount);
    }

    [Fact]
    public async Task Valid_json_is_bound_with_generated_metadata_before_explicit_adapter_invocation()
    {
        var adapter = Adapter(Send);
        var catalog = Catalog(
            [Send],
            [Registration(Send, adapter)],
            allowed: [Send.Id]);

        var receipt = await catalog.InvokeAsync(
            Send.Id,
            Json("{\"message\":\"hello\"}"),
            Invocation(Grant()),
            TestContext.Current.CancellationToken);

        Assert.Equal(Send.Id, receipt.Operation);
        Assert.Equal(1, adapter.InvocationCount);
        Assert.Equal("hello", adapter.LastInput.GetProperty("message").GetString());
    }

    [Fact]
    public void Product_invocation_context_rejects_a_missing_idempotency_key()
    {
        Assert.Throws<ArgumentException>(() => new ProductInvocationContext(Grant(), default));
    }

    private static ProductOperationCatalog Catalog(
        IReadOnlyCollection<OperationDescriptor> operations,
        IReadOnlyCollection<ProductOperationRegistration> registrations,
        IReadOnlyCollection<OperationId> allowed)
        => new(Registry(operations), Filter(allowed), registrations);

    private static ModuleRegistry Registry(IReadOnlyCollection<OperationDescriptor> operations)
    {
        var manifest = new ModuleManifest(
            Module,
            new ModuleVersion(1, 0, 0),
            [],
            [new NeuronRoleDescriptor(EntryRole, NeuronScope.Workspace, Module)],
            operations,
            [],
            [],
            [],
            [],
            []);
        var registry = new ModuleRegistry();
        registry.Resolve([manifest]);
        return registry;
    }

    private static ProductOperationPolicyFilter Filter(IReadOnlyCollection<OperationId> allowed)
        => new(
            new FixturePolicyEvaluator(allowed),
            new FixturePolicyVersionProvider(Workspace, currentVersion: 7),
            new FixedTimeProvider(IssuedAt.AddMinutes(1)));

    private static ProductOperationRegistration Registration(
        OperationDescriptor operation,
        FixtureProductOperationAdapter adapter)
        => new(
            operation,
            adapter,
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            new ProductOperationAccessPolicy(["member"], ["operations.invoke"]));

    private static FixtureProductOperationAdapter Adapter(OperationDescriptor operation)
        => new(new ProductOperationDescriptor(
            operation,
            operation.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\"],\"properties\":{\"message\":{\"type\":\"string\"}}}",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\"}}}"));

    private static OperationDescriptor Operation(string id)
    {
        var name = id[(id.IndexOf('/', StringComparison.Ordinal) + 1)..id.LastIndexOf('@')];
        return new OperationDescriptor(
            new OperationId(id),
            new ContractId($"conversation/{name}-input@1"),
            new ContractId($"conversation/{name}-result@1"),
            EntryRole,
            Module,
            new ContractVersion(1));
    }

    private static BrainAccessGrant Grant(
        WorkspaceId? workspace = null,
        int policyVersion = 7,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<string>? grants = null)
        => BrainAccessGrant.Create(
            workspace ?? Workspace,
            Principal,
            roles ?? ["member"],
            grants ?? ["operations.invoke"],
            [],
            policyVersion,
            IssuedAt,
            IssuedAt.AddMinutes(10),
            IssuedAt);

    private static ProductInvocationContext Invocation(BrainAccessGrant grant)
        => new(grant, new IdempotencyKey("request-1"));

    private static JsonElement Json(string value)
        => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class FixtureProductOperationAdapter(ProductOperationDescriptor descriptor)
        : IProductOperationAdapter
    {
        public IReadOnlyList<ProductOperationDescriptor> Operations { get; } = [descriptor];

        public int InvocationCount { get; private set; }

        public JsonElement LastInput { get; private set; }

        public Task<ProductActivityReceipt> InvokeAsync(
            OperationId operation,
            JsonElement input,
            ProductInvocationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            LastInput = input.Clone();
            return Task.FromResult(new ProductActivityReceipt(BrainActivityId.New(), operation));
        }

        public Task<ProductActivityProjection> ObserveAsync(
            BrainActivityId activity,
            ProductInvocationContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FixturePolicyEvaluator(IReadOnlyCollection<OperationId> allowed)
        : IWorkspacePolicyEvaluator
    {
        private readonly HashSet<OperationId> _allowed = [.. allowed];

        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation)
            => _allowed.Contains(operation.Id) ? PolicyDecision.Allowed : PolicyDecision.Refused;

        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request)
            => PolicyDecision.Refused;

        public PolicyDecision AuthorizeCapability(ActivityContext context, CapabilityDescriptor capability)
            => PolicyDecision.Refused;
    }

    private sealed class FixturePolicyVersionProvider(WorkspaceId workspace, int currentVersion)
        : IWorkspacePolicyVersionProvider
    {
        public bool TryGetCurrentVersion(WorkspaceId requestedWorkspace, out int policyVersion)
        {
            policyVersion = currentVersion;
            return requestedWorkspace == workspace;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

internal sealed record CatalogInput(string Message);

internal sealed record CatalogResult(string Result);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CatalogInput))]
[JsonSerializable(typeof(CatalogResult))]
internal sealed partial class CatalogJsonSerializerContext : JsonSerializerContext;
