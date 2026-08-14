using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
            adapter.Binding,
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

    [Fact]
    public async Task Trusted_principal_kind_is_preserved_for_core_policy()
    {
        var adapter = Adapter(Send);
        var policy = new ServiceOnlyPolicyEvaluator();
        var filter = new ProductOperationPolicyFilter(
            policy,
            new FixturePolicyVersionProvider(Workspace, currentVersion: 7),
            new FixedTimeProvider(IssuedAt.AddMinutes(1)));
        var catalog = new ProductOperationCatalog(
            Registry([Send]),
            filter,
            [Registration(Send, adapter)]);
        var human = Grant(principalKind: BrainPrincipalKind.Human);
        var service = Grant(principalKind: BrainPrincipalKind.Service);

        Assert.Empty(await catalog.DiscoverAsync(human, TestContext.Current.CancellationToken));
        Assert.Single(await catalog.DiscoverAsync(service, TestContext.Current.CancellationToken));
        await catalog.InvokeAsync(
            Send.Id,
            Json("{\"message\":\"service-call\"}"),
            Invocation(service),
            TestContext.Current.CancellationToken);

        Assert.Contains(false, policy.ObservedKinds);
        Assert.Contains(true, policy.ObservedKinds);
        Assert.Equal(1, adapter.InvocationCount);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Module_removal_or_replacement_after_catalog_construction_fails_closed(
        bool replaceModule)
    {
        var adapter = Adapter(Send);
        var registry = Registry([Send]);
        var catalog = new ProductOperationCatalog(
            registry,
            Filter([Send.Id]),
            [Registration(Send, adapter)]);

        registry.Resolve(replaceModule ? [Manifest([Send], new ModuleVersion(1, 1, 0))] : []);

        Assert.Empty(await catalog.DiscoverAsync(Grant(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ProductOperationNotAvailableException>(() => catalog.InvokeAsync(
            Send.Id,
            Json("{\"message\":\"hello\"}"),
            Invocation(Grant()),
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

    [Theory]
    [InlineData("conversation.send@1", "conversation", 1)]
    [InlineData("external/send@1", "conversation", 1)]
    [InlineData("conversation/send@2", "conversation", 1)]
    [InlineData("conversation/Send@1", "conversation", 1)]
    public void Malformed_owner_mismatched_or_major_mismatched_operation_identity_is_rejected(
        string id,
        string owner,
        int version)
    {
        var operation = AdversarialOperation(id, new ModuleId(owner), version);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            Registration(operation, Adapter(operation)));
    }

    [Fact]
    public void Two_versions_that_map_to_one_northbound_identity_are_rejected()
    {
        var versionOne = Operation("conversation/send@1", version: 1);
        var versionTwo = Operation("conversation/send@2", version: 2);
        var registry = Registry([versionOne, versionTwo]);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() => new ProductOperationCatalog(
            registry,
            Filter([versionOne.Id, versionTwo.Id]),
            [Registration(versionOne, Adapter(versionOne)), Registration(versionTwo, Adapter(versionTwo))]));
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
    public void Swapped_or_schema_mismatched_generated_metadata_is_rejected()
    {
        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            new ProductJsonAdapter<CatalogResult, CatalogInput>(
                Descriptor(Send),
                CatalogJsonSerializerContext.Default.CatalogResult,
                CatalogJsonSerializerContext.Default.CatalogInput,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveNotSupported));

        var mismatchedDescriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\"}}}",
            Descriptor(Send).TerminalResultSchema);
        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            new ProductJsonAdapter<CatalogInput, CatalogResult>(
                mismatchedDescriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveNotSupported));
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[],\"properties\":{\"message\":{\"type\":\"string\"}}}")]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\",\"message\"],\"properties\":{\"message\":{\"type\":\"string\"}}}")]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\"],\"properties\":{\"message\":{\"type\":\"string\"},\"message\":{\"type\":\"string\"}}}")]
    [InlineData("{\"type\":\"object\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\"],\"properties\":{\"message\":{\"type\":\"string\"}}}")]
    public void Schema_required_and_duplicate_members_must_match_generated_metadata(string inputSchema)
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            inputSchema,
            Descriptor(Send).TerminalResultSchema);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            new ProductJsonAdapter<CatalogInput, CatalogResult>(
                descriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveNotSupported));
    }

    [Fact]
    public void Hostile_generated_json_contract_shapes_are_rejected()
    {
        AssertHostileMetadataRejected(
            HostileJsonSerializerContext.Default.ExtensionDataInput,
            "{\"message\":{\"type\":\"string\"}}");
        AssertHostileMetadataRejected(
            HostileJsonSerializerContext.Default.PerTypeSkipInput,
            "{\"message\":{\"type\":\"string\"}}");
        AssertHostileMetadataRejected(
            HostileJsonSerializerContext.Default.PolymorphicInput,
            "{}");
        AssertHostileMetadataRejected(
            HostileJsonSerializerContext.Default.OpenObjectInput,
            "{\"payload\":{\"type\":\"object\"}}");
        AssertHostileMetadataRejected(
            HostileJsonSerializerContext.Default.JsonElementInput,
            "{\"payload\":{\"type\":\"object\"}}");
        AssertHostileMetadataRejected(
            HostileJsonSerializerContext.Default.CustomConverterInput,
            "{\"message\":{\"type\":\"string\"}}");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"message\":\"first\",\"message\":\"second\"}")]
    public async Task Missing_required_or_duplicate_properties_are_rejected_before_typed_invocation(
        string input)
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
        Assert.Equal("hello", adapter.LastInput?.Message);
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
        var registry = new ModuleRegistry();
        registry.Resolve([Manifest(operations)]);
        return registry;
    }

    private static ModuleManifest Manifest(
        IReadOnlyCollection<OperationDescriptor> operations,
        ModuleVersion? version = null)
        => new(
            Module,
            version ?? new ModuleVersion(1, 0, 0),
            [],
            [new NeuronRoleDescriptor(EntryRole, NeuronScope.Workspace, Module)],
            operations,
            [],
            [],
            [],
            [],
            []);

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
            adapter.Binding,
            new ProductOperationAccessPolicy(["member"], ["operations.invoke"]));

    private static FixtureProductOperationAdapter Adapter(OperationDescriptor operation)
        => new(Descriptor(operation));

    private static ProductOperationDescriptor Descriptor(OperationDescriptor operation)
        => new(
            operation,
            operation.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\"],\"properties\":{\"message\":{\"type\":\"string\"}}}",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\"}}}");

    private static void AssertHostileMetadataRejected<TInput>(
        JsonTypeInfo<TInput> inputType,
        string properties)
        where TInput : class
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            $"{{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{properties}}}",
            Descriptor(Send).TerminalResultSchema);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            new ProductJsonAdapter<TInput, CatalogResult>(
                descriptor,
                inputType,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveNotSupported));
    }

    private static Task<ProductActivityProjection> ObserveNotSupported(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    private static OperationDescriptor Operation(string id, int version = 1)
    {
        var name = id[(id.IndexOf('/', StringComparison.Ordinal) + 1)..id.LastIndexOf('@')];
        return new OperationDescriptor(
            new OperationId(id),
            new ContractId($"conversation/{name}-input@{version}"),
            new ContractId($"conversation/{name}-result@{version}"),
            EntryRole,
            Module,
            new ContractVersion(version));
    }

    private static OperationDescriptor AdversarialOperation(string id, ModuleId owner, int version)
        => new(
            new OperationId(id),
            new ContractId("conversation/adversarial-input@1"),
            new ContractId("conversation/adversarial-result@1"),
            EntryRole,
            owner,
            new ContractVersion(version));

    private static BrainAccessGrant Grant(
        WorkspaceId? workspace = null,
        int policyVersion = 7,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<string>? grants = null,
        BrainPrincipalKind principalKind = BrainPrincipalKind.Human)
        => BrainAccessGrant.Create(
            workspace ?? Workspace,
            Principal,
            principalKind,
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

    private sealed class FixtureProductOperationAdapter
    {
        public FixtureProductOperationAdapter(ProductOperationDescriptor descriptor)
        {
            Binding = new ProductJsonAdapter<CatalogInput, CatalogResult>(
                descriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                InvokeTypedAsync,
                ObserveNotSupported);
        }

        public ProductJsonAdapter<CatalogInput, CatalogResult> Binding { get; }

        public int InvocationCount { get; private set; }

        public CatalogInput? LastInput { get; private set; }

        private Task<ProductActivityReceipt> InvokeTypedAsync(
            CatalogInput input,
            ProductInvocationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            LastInput = input;
            return Task.FromResult(new ProductActivityReceipt(BrainActivityId.New(), Binding.Descriptor.Operation.Id));
        }
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

    private sealed class ServiceOnlyPolicyEvaluator : IWorkspacePolicyEvaluator
    {
        public List<bool> ObservedKinds { get; } = [];

        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation)
        {
            ObservedKinds.Add(caller.IsServicePrincipal);
            return caller.IsServicePrincipal ? PolicyDecision.Allowed : PolicyDecision.Refused;
        }

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
    AllowDuplicateProperties = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CatalogInput))]
[JsonSerializable(typeof(CatalogResult))]
internal sealed partial class CatalogJsonSerializerContext : JsonSerializerContext;

internal sealed record ExtensionDataInput(string Message)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement> ExtensionData { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record PerTypeSkipInput(string Message);

[JsonPolymorphic]
[JsonDerivedType(typeof(PolymorphicDerivedInput), "derived")]
internal abstract record PolymorphicInput;

internal sealed record PolymorphicDerivedInput(string Message) : PolymorphicInput;

internal sealed record OpenObjectInput(object Payload);

internal sealed record JsonElementInput(JsonElement Payload);

internal sealed record CustomConverterInput(
    [property: JsonConverter(typeof(TrimStringJsonConverter))] string Message);

internal sealed class TrimStringJsonConverter : JsonConverter<string>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString()?.Trim();

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ExtensionDataInput))]
[JsonSerializable(typeof(PerTypeSkipInput))]
[JsonSerializable(typeof(PolymorphicInput))]
[JsonSerializable(typeof(OpenObjectInput))]
[JsonSerializable(typeof(JsonElementInput))]
[JsonSerializable(typeof(CustomConverterInput))]
internal sealed partial class HostileJsonSerializerContext : JsonSerializerContext;
