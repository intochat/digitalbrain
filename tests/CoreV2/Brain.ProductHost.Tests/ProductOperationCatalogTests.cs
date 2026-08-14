using System.Diagnostics.CodeAnalysis;
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
    public async Task Discovery_holds_the_active_module_snapshot_until_descriptor_exposure_finishes()
    {
        using var policyEntered = new ManualResetEventSlim();
        using var releasePolicy = new ManualResetEventSlim();
        using var mutationStarted = new ManualResetEventSlim();
        var registry = Registry([Send]);
        var filter = new ProductOperationPolicyFilter(
            new BlockingPolicyEvaluator([Send.Id], policyEntered, releasePolicy),
            new FixturePolicyVersionProvider(Workspace, currentVersion: 7),
            new FixedTimeProvider(IssuedAt.AddMinutes(1)));
        var catalog = new ProductOperationCatalog(
            registry,
            filter,
            [Registration(Send, Adapter(Send))]);
        var discovery = Task.Run(
            () => catalog.DiscoverAsync(Grant(), TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.True(policyEntered.Wait(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));
        var mutation = Task.Run(() =>
        {
            mutationStarted.Set();
            registry.Resolve([]);
        }, TestContext.Current.CancellationToken);
        Assert.True(mutationStarted.Wait(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.False(mutation.IsCompleted);
        }
        finally
        {
            releasePolicy.Set();
        }

        Assert.Single(await discovery);
        await mutation;
        Assert.Empty(await catalog.DiscoverAsync(Grant(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invocation_holds_the_active_module_snapshot_through_typed_adapter_entry()
    {
        using var policyEntered = new ManualResetEventSlim();
        using var releasePolicy = new ManualResetEventSlim();
        using var mutationStarted = new ManualResetEventSlim();
        var sequence = 0L;
        var adapterOrder = 0L;
        var mutationOrder = 0L;
        var adapter = Adapter(Send, () => adapterOrder = Interlocked.Increment(ref sequence));
        var registry = Registry([Send]);
        var filter = new ProductOperationPolicyFilter(
            new BlockingPolicyEvaluator([Send.Id], policyEntered, releasePolicy),
            new FixturePolicyVersionProvider(Workspace, currentVersion: 7),
            new FixedTimeProvider(IssuedAt.AddMinutes(1)));
        var catalog = new ProductOperationCatalog(registry, filter, [Registration(Send, adapter)]);
        var invocation = Task.Run(
            () => catalog.InvokeAsync(
                Send.Id,
                Json("{\"message\":\"hello\"}"),
                Invocation(Grant()),
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.True(policyEntered.Wait(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));
        var mutation = Task.Run(() =>
        {
            mutationStarted.Set();
            registry.Resolve([Manifest([Send], new ModuleVersion(1, 1, 0))]);
            mutationOrder = Interlocked.Increment(ref sequence);
        }, TestContext.Current.CancellationToken);
        Assert.True(mutationStarted.Wait(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.False(mutation.IsCompleted);
        }
        finally
        {
            releasePolicy.Set();
        }

        await invocation;
        await mutation;
        Assert.True(adapterOrder > 0);
        Assert.True(adapterOrder < mutationOrder);
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
            ProductOperationBinding.Create<CatalogResult, CatalogInput>(
                Descriptor(Send),
                CatalogJsonSerializerContext.Default.CatalogResult,
                CatalogJsonSerializerContext.Default.CatalogInput,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveTypedNotSupported<CatalogInput>));

        var mismatchedDescriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\"}}}",
            Descriptor(Send).TerminalResultSchema);
        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            ProductOperationBinding.Create<CatalogInput, CatalogResult>(
                mismatchedDescriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveTypedNotSupported<CatalogResult>));
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
            ProductOperationBinding.Create<CatalogInput, CatalogResult>(
                descriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveTypedNotSupported<CatalogResult>));
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

    [Fact]
    public void Registration_admits_only_a_sealed_binding_without_a_public_raw_json_override()
    {
        var bindingType = typeof(ProductOperationBinding);
        Assert.True(bindingType.IsSealed);

        var constructor = Assert.Single(typeof(ProductOperationRegistration).GetConstructors());
        Assert.Equal(bindingType, constructor.GetParameters()[1].ParameterType);
        Assert.DoesNotContain(
            bindingType.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly),
            static method => method.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(JsonElement)));
    }

    [Fact]
    public async Task Terminal_result_is_serialized_only_with_the_declared_generated_metadata()
    {
        var activity = ObservedActivity(BrainActivityId.New(), ActivityStatus.Completed);
        var binding = ProductOperationBinding.Create<CatalogInput, CatalogResult>(
            Descriptor(Send),
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            static (_, _, _) => throw new NotSupportedException(),
            (_, _, _) => Task.FromResult(
                new ProductOperationObservation<CatalogResult>(
                    activity,
                    progress: null,
                    new CatalogResult("done"))));

        var projection = await ((IProductOperationAdapter)binding).ObserveAsync(
            activity.Activity,
            Invocation(Grant()),
            TestContext.Current.CancellationToken);

        Assert.Equal("done", projection.Result?.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Hostile_terminal_result_is_rejected_before_raw_projection_exposure()
    {
        var activity = ObservedActivity(BrainActivityId.New(), ActivityStatus.Completed);
        var binding = ProductOperationBinding.Create<CatalogInput, CatalogResult>(
            Descriptor(Send),
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            static (_, _, _) => throw new NotSupportedException(),
            (_, _, _) => Task.FromResult(
                new ProductOperationObservation<CatalogResult>(
                    activity,
                    progress: null,
                    new CatalogResult(null!))));

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                activity.Activity,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ActivityStatus.Completed, false)]
    [InlineData(ActivityStatus.Accepted, true)]
    public async Task Observation_status_and_terminal_result_must_describe_one_coherent_lifecycle(
        ActivityStatus status,
        bool includeResult)
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(requested, status);
        var binding = ProductOperationBinding.Create<CatalogInput, CatalogResult>(
            Descriptor(Send),
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            static (_, _, _) => throw new NotSupportedException(),
            (_, _, _) => Task.FromResult(
                new ProductOperationObservation<CatalogResult>(
                    activity,
                    progress: null,
                    includeResult ? new CatalogResult("done") : null)));

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                requested,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("activity")]
    [InlineData("operation")]
    [InlineData("terminal-contract")]
    public async Task Observation_identity_must_match_the_requested_bound_operation(string mismatch)
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(
            mismatch == "activity" ? BrainActivityId.New() : requested,
            ActivityStatus.Completed,
            mismatch == "operation" ? new OperationId("conversation/other@1") : Send.Id,
            mismatch == "terminal-contract"
                ? new ContractId("conversation/other-result@1")
                : Send.TerminalResultContract);
        var binding = ProductOperationBinding.Create<CatalogInput, CatalogResult>(
            Descriptor(Send),
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            static (_, _, _) => throw new NotSupportedException(),
            (_, _, _) => Task.FromResult(
                new ProductOperationObservation<CatalogResult>(
                    activity,
                    progress: null,
                    new CatalogResult("done"))));

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                requested,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ActivityStatus.Refused)]
    [InlineData(ActivityStatus.Failed)]
    [InlineData(ActivityStatus.Cancelled)]
    public async Task Error_terminal_states_forbid_embedded_result_references(ActivityStatus status)
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(requested, status) with
        {
            Result = ResultReference(Send.TerminalResultContract),
        };
        var binding = BindingForObservation(activity, result: null);

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                requested,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Completed_activity_result_reference_must_match_the_bound_terminal_contract()
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(requested, ActivityStatus.Completed) with
        {
            Result = ResultReference(new ContractId("conversation/other-result@1")),
        };
        var binding = BindingForObservation(activity, new CatalogResult("done"));

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                requested,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Undefined_activity_status_is_rejected()
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(requested, (ActivityStatus)999);
        var binding = BindingForObservation(activity, result: null);

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                requested,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ActivityStatus.Accepted, true)]
    [InlineData(ActivityStatus.Running, true)]
    [InlineData(ActivityStatus.AwaitingConfirmation, true)]
    [InlineData(ActivityStatus.Completed, true)]
    [InlineData(ActivityStatus.Refused, false)]
    [InlineData(ActivityStatus.Failed, false)]
    [InlineData(ActivityStatus.Cancelled, false)]
    public async Task Activity_problem_must_match_the_lifecycle_state(
        ActivityStatus status,
        bool includeProblem)
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(requested, status) with
        {
            Problem = includeProblem ? new ActivityProblem("hostile", "contradiction") : null,
        };
        var result = status == ActivityStatus.Completed ? new CatalogResult("done") : null;
        var binding = BindingForObservation(activity, result);

        await Assert.ThrowsAsync<ProductOperationResultException>(() =>
            ((IProductOperationAdapter)binding).ObserveAsync(
                requested,
                Invocation(Grant()),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ActivityStatus.Accepted)]
    [InlineData(ActivityStatus.Running)]
    [InlineData(ActivityStatus.AwaitingConfirmation)]
    [InlineData(ActivityStatus.Completed)]
    [InlineData(ActivityStatus.Refused)]
    [InlineData(ActivityStatus.Failed)]
    [InlineData(ActivityStatus.Cancelled)]
    public async Task Every_coherent_activity_lifecycle_state_is_projected(ActivityStatus status)
    {
        var requested = BrainActivityId.New();
        var activity = ObservedActivity(requested, status);
        var result = status == ActivityStatus.Completed ? new CatalogResult("done") : null;
        var binding = BindingForObservation(activity, result);

        var projection = await ((IProductOperationAdapter)binding).ObserveAsync(
            requested,
            Invocation(Grant()),
            TestContext.Current.CancellationToken);

        Assert.Equal(status, projection.Activity.Status);
        Assert.Equal(status == ActivityStatus.Completed, projection.Result.HasValue);
    }

    [Theory]
    [InlineData("pattern")]
    [InlineData("enum")]
    [InlineData("nested-object")]
    [InlineData("array-items")]
    [InlineData("nullability")]
    [InlineData("nested-duplicate")]
    public void Terminal_schema_rejects_unenforced_or_recursively_mismatched_shapes(string hostileShape)
    {
        var (terminalSchema, terminalType) = hostileShape switch
        {
            "pattern" => (
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\",\"pattern\":\"^ok$\"}}}",
                (JsonTypeInfo)CatalogJsonSerializerContext.Default.CatalogResult),
            "enum" => (
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\",\"enum\":[\"ok\"]}}}",
                CatalogJsonSerializerContext.Default.CatalogResult),
            "nested-object" => (
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"payload\"],\"properties\":{\"payload\":{\"type\":\"object\"}}}",
                CatalogJsonSerializerContext.Default.NestedCatalogResult),
            "array-items" => (
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"items\"],\"properties\":{\"items\":{\"type\":\"array\"}}}",
                CatalogJsonSerializerContext.Default.ArrayCatalogResult),
            "nullability" => (
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\"}}}",
                CatalogJsonSerializerContext.Default.NullableCatalogResult),
            "nested-duplicate" => (
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"payload\"],\"properties\":{\"payload\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"}}}}}",
                CatalogJsonSerializerContext.Default.NestedCatalogResult),
            _ => throw new ArgumentOutOfRangeException(nameof(hostileShape)),
        };
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            Descriptor(Send).InputSchema,
            terminalSchema);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            CreateBindingWithTerminalMetadata(descriptor, terminalType));
    }

    [Fact]
    public void Populate_handled_initialized_collections_are_rejected_before_input_binding()
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[],\"properties\":{\"items\":{\"type\":[\"array\",\"null\"],\"items\":{\"type\":\"integer\"}}}}",
            Descriptor(Send).TerminalResultSchema);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            ProductOperationBinding.Create<PopulateCollectionInput, CatalogResult>(
                descriptor,
                HostileJsonSerializerContext.Default.PopulateCollectionInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new NotSupportedException(),
                ObserveTypedNotSupported<CatalogResult>));
    }

    [Theory]
    [InlineData("seeded")]
    [InlineData("callback")]
    [InlineData("number-handling")]
    public void Nested_collection_construction_and_type_behaviors_are_rejected(string hostileShape)
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"items\"],\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}}}",
            Descriptor(Send).TerminalResultSchema);

        switch (hostileShape)
        {
            case "seeded":
                Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
                    ProductOperationBinding.Create<SeededCollectionInput, CatalogResult>(
                        descriptor,
                        HostileJsonSerializerContext.Default.SeededCollectionInput,
                        CatalogJsonSerializerContext.Default.CatalogResult,
                        static (_, _, _) => throw new InvalidOperationException(),
                        ObserveTypedNotSupported<CatalogResult>));
                break;
            case "callback":
                Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
                    ProductOperationBinding.Create<CallbackCollectionInput, CatalogResult>(
                        descriptor,
                        HostileJsonSerializerContext.Default.CallbackCollectionInput,
                        CatalogJsonSerializerContext.Default.CatalogResult,
                        static (_, _, _) => throw new InvalidOperationException(),
                        ObserveTypedNotSupported<CatalogResult>));
                break;
            case "number-handling":
                Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
                    ProductOperationBinding.Create<NumberHandledCollectionInput, CatalogResult>(
                        descriptor,
                        HostileJsonSerializerContext.Default.NumberHandledCollectionInput,
                        CatalogJsonSerializerContext.Default.CatalogResult,
                        static (_, _, _) => throw new InvalidOperationException(),
                        ObserveTypedNotSupported<CatalogResult>));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hostileShape));
        }
    }

    [Fact]
    public async Task Accepted_metadata_graph_is_frozen_before_validation_and_cannot_be_reinterpreted()
    {
        var options = new JsonSerializerOptions
        {
            AllowDuplicateProperties = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var resolver = new MutableGraphResolver();
        options.TypeInfoResolver = resolver;
        var inputType = JsonTypeInfo.CreateJsonTypeInfo<MutableGraphInput>(options);
        inputType.CreateObject = static () => new MutableGraphInput();
        inputType.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        var inputPayload = inputType.CreateJsonPropertyInfo(typeof(MutableGraphPayload), "payload");
        inputPayload.Get = static value => ((MutableGraphInput)value).Payload;
        inputPayload.Set = static (value, payload) =>
            ((MutableGraphInput)value).Payload = (MutableGraphPayload)payload!;
        inputPayload.IsRequired = true;
        inputPayload.IsGetNullable = false;
        inputPayload.IsSetNullable = false;
        inputType.Properties.Add(inputPayload);

        var nestedType = JsonTypeInfo.CreateJsonTypeInfo<MutableGraphPayload>(options);
        nestedType.CreateObject = static () => new MutableGraphPayload();
        nestedType.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        var nestedValue = nestedType.CreateJsonPropertyInfo(typeof(string), "value");
        nestedValue.Get = static value => ((MutableGraphPayload)value).Value;
        nestedValue.Set = static (value, property) =>
            ((MutableGraphPayload)value).Value = (string)property!;
        nestedValue.IsRequired = true;
        nestedValue.IsGetNullable = false;
        nestedValue.IsSetNullable = false;
        nestedType.Properties.Add(nestedValue);

        var replacementNestedType = JsonTypeInfo.CreateJsonTypeInfo<MutableGraphPayload>(options);
        replacementNestedType.CreateObject = static () => new MutableGraphPayload();
        replacementNestedType.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        var replacementValue = replacementNestedType.CreateJsonPropertyInfo(typeof(string), "value");
        replacementValue.Get = static value => ((MutableGraphPayload)value).Value;
        replacementValue.Set = static (value, property) =>
            ((MutableGraphPayload)value).Value = $"reinterpreted:{property}";
        replacementValue.IsRequired = true;
        replacementValue.IsGetNullable = false;
        replacementValue.IsSetNullable = false;
        replacementNestedType.Properties.Add(replacementValue);

        var resultType = JsonTypeInfo.CreateJsonTypeInfo<MutableGraphResult>(options);
        resultType.CreateObject = static () => new MutableGraphResult();
        resultType.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        var resultValue = resultType.CreateJsonPropertyInfo(typeof(string), "result");
        resultValue.Get = static value => ((MutableGraphResult)value).Result;
        resultValue.Set = static (value, property) =>
            ((MutableGraphResult)value).Result = (string)property!;
        resultValue.IsRequired = true;
        resultValue.IsGetNullable = false;
        resultValue.IsSetNullable = false;
        resultType.Properties.Add(resultValue);

        resolver.Add(inputType);
        resolver.Add(nestedType);
        resolver.Add(resultType);
        resolver.Add(JsonTypeInfo.CreateJsonTypeInfo<string>(options));
        Assert.False(inputType.IsReadOnly);
        Assert.False(nestedType.IsReadOnly);

        var invoked = 0;
        string? observedInput = null;
        var activity = ObservedActivity(BrainActivityId.New(), ActivityStatus.Completed);
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"payload\"],\"properties\":{\"payload\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"string\"}}}}}",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"result\"],\"properties\":{\"result\":{\"type\":\"string\"}}}");
        var binding = ProductOperationBinding.Create<MutableGraphInput, MutableGraphResult>(
            descriptor,
            inputType,
            resultType,
            (input, _, _) =>
            {
                invoked++;
                observedInput = input.Payload.Value;
                return Task.FromResult(new ProductActivityReceipt(activity.Activity, Send.Id));
            },
            (_, _, _) => Task.FromResult(
                new ProductOperationObservation<MutableGraphResult>(
                    activity,
                    progress: null,
                    new MutableGraphResult("done"))));

        Assert.True(inputType.IsReadOnly);
        Assert.True(nestedType.IsReadOnly);
        Assert.True(resultType.IsReadOnly);
        Assert.True(inputType.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => nestedType.OnDeserialized = _ => { });
        Assert.Throws<InvalidOperationException>(() => inputType.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip);
        Assert.Throws<InvalidOperationException>(() => inputType.Options.PropertyNameCaseInsensitive = true);
        resolver.Replace(replacementNestedType);

        var adapter = (IProductOperationAdapter)binding;
        await Assert.ThrowsAsync<ProductOperationInputException>(() => adapter.InvokeAsync(
            Send.Id,
            Json("{\"payload\":{\"value\":\"ok\",\"extra\":true}}"),
            Invocation(Grant()),
            TestContext.Current.CancellationToken));
        Assert.Equal(0, invoked);

        await adapter.InvokeAsync(
            Send.Id,
            Json("{\"payload\":{\"value\":\"ok\"}}"),
            Invocation(Grant()),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, invoked);
        Assert.Equal("ok", observedInput);

        var projection = await adapter.ObserveAsync(
            activity.Activity,
            Invocation(Grant()),
            TestContext.Current.CancellationToken);
        Assert.Equal("done", projection.Result?.GetProperty("result").GetString());
    }

    [Fact]
    public void Input_nullability_uses_the_generated_setter_contract_only()
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[],\"properties\":{\"value\":{\"type\":[\"string\",\"null\"]}}}",
            Descriptor(Send).TerminalResultSchema);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            ProductOperationBinding.Create<SetterDisallowsNullInput, CatalogResult>(
                descriptor,
                HostileJsonSerializerContext.Default.SetterDisallowsNullInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new NotSupportedException(),
                ObserveTypedNotSupported<CatalogResult>));
    }

    [Fact]
    public void Terminal_nullability_uses_the_generated_getter_contract_only()
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            Descriptor(Send).InputSchema,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[],\"properties\":{\"value\":{\"type\":[\"string\",\"null\"]}}}");

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            ProductOperationBinding.Create<CatalogInput, SetterAllowsNullResult>(
                descriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                HostileJsonSerializerContext.Default.SetterAllowsNullResult,
                static (_, _, _) => throw new NotSupportedException(),
                ObserveTypedNotSupported<SetterAllowsNullResult>));
    }

    [Fact]
    public void Reference_collection_element_nullability_must_be_provable()
    {
        var descriptor = new ProductOperationDescriptor(
            Send,
            Send.Id.Value,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"items\"],\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}",
            Descriptor(Send).TerminalResultSchema);

        Assert.Throws<ProductOperationCatalogConfigurationException>(() =>
            ProductOperationBinding.Create<ReferenceCollectionInput, CatalogResult>(
                descriptor,
                HostileJsonSerializerContext.Default.ReferenceCollectionInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new NotSupportedException(),
                ObserveTypedNotSupported<CatalogResult>));
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

    private static FixtureProductOperationAdapter Adapter(
        OperationDescriptor operation,
        Action? onInvoke = null)
        => new(Descriptor(operation), onInvoke);

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
            ProductOperationBinding.Create<TInput, CatalogResult>(
                descriptor,
                inputType,
                CatalogJsonSerializerContext.Default.CatalogResult,
                static (_, _, _) => throw new InvalidOperationException(),
                ObserveTypedNotSupported<CatalogResult>));
    }

    private static ProductOperationBinding CreateBindingWithTerminalMetadata(
        ProductOperationDescriptor descriptor,
        JsonTypeInfo terminalType)
        => terminalType.Type == typeof(CatalogResult)
            ? ProductOperationBinding.Create<CatalogInput, CatalogResult>(
                descriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                (JsonTypeInfo<CatalogResult>)terminalType,
                static (_, _, _) => throw new NotSupportedException(),
                ObserveTypedNotSupported<CatalogResult>)
            : terminalType.Type == typeof(NestedCatalogResult)
                ? ProductOperationBinding.Create<CatalogInput, NestedCatalogResult>(
                    descriptor,
                    CatalogJsonSerializerContext.Default.CatalogInput,
                    (JsonTypeInfo<NestedCatalogResult>)terminalType,
                    static (_, _, _) => throw new NotSupportedException(),
                    ObserveTypedNotSupported<NestedCatalogResult>)
                : terminalType.Type == typeof(ArrayCatalogResult)
                    ? ProductOperationBinding.Create<CatalogInput, ArrayCatalogResult>(
                        descriptor,
                        CatalogJsonSerializerContext.Default.CatalogInput,
                        (JsonTypeInfo<ArrayCatalogResult>)terminalType,
                        static (_, _, _) => throw new NotSupportedException(),
                        ObserveTypedNotSupported<ArrayCatalogResult>)
                    : ProductOperationBinding.Create<CatalogInput, NullableCatalogResult>(
                        descriptor,
                        CatalogJsonSerializerContext.Default.CatalogInput,
                        (JsonTypeInfo<NullableCatalogResult>)terminalType,
                        static (_, _, _) => throw new NotSupportedException(),
                        ObserveTypedNotSupported<NullableCatalogResult>);

    private static Task<ProductOperationObservation<TResult>> ObserveTypedNotSupported<TResult>(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
        where TResult : class
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

    private static ActivityView ObservedActivity(
        BrainActivityId activity,
        ActivityStatus status,
        OperationId? operation = null,
        ContractId? terminalResultContract = null)
    {
        var contract = terminalResultContract ?? Send.TerminalResultContract;
        return new ActivityView(
            activity,
            operation ?? Send.Id,
            status,
            contract,
            Progress: null,
            Result: status == ActivityStatus.Completed ? ResultReference(contract) : null,
            Problem: status is ActivityStatus.Refused or ActivityStatus.Failed or ActivityStatus.Cancelled
                ? new ActivityProblem("terminal", "The activity ended without a result.")
                : null);
    }

    private static ProductOperationBinding BindingForObservation(
        ActivityView activity,
        CatalogResult? result)
        => ProductOperationBinding.Create<CatalogInput, CatalogResult>(
            Descriptor(Send),
            CatalogJsonSerializerContext.Default.CatalogInput,
            CatalogJsonSerializerContext.Default.CatalogResult,
            static (_, _, _) => throw new NotSupportedException(),
            (_, _, _) => Task.FromResult(
                new ProductOperationObservation<CatalogResult>(activity, progress: null, result)));

    private static ActivityResultReference ResultReference(ContractId contract)
        => new(contract, new ActivityPayloadReference("result-ref"));

    private static JsonElement Json(string value)
        => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class FixtureProductOperationAdapter
    {
        private readonly Action? _onInvoke;

        public FixtureProductOperationAdapter(ProductOperationDescriptor descriptor, Action? onInvoke = null)
        {
            _onInvoke = onInvoke;
            Binding = ProductOperationBinding.Create<CatalogInput, CatalogResult>(
                descriptor,
                CatalogJsonSerializerContext.Default.CatalogInput,
                CatalogJsonSerializerContext.Default.CatalogResult,
                InvokeTypedAsync,
                ObserveTypedNotSupported<CatalogResult>);
        }

        public ProductOperationBinding Binding { get; }

        public int InvocationCount { get; private set; }

        public CatalogInput? LastInput { get; private set; }

        private Task<ProductActivityReceipt> InvokeTypedAsync(
            CatalogInput input,
            ProductInvocationContext _,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onInvoke?.Invoke();
            InvocationCount++;
            LastInput = input;
            return Task.FromResult(new ProductActivityReceipt(BrainActivityId.New(), Binding.Descriptor.Operation.Id));
        }
    }

    private sealed class BlockingPolicyEvaluator(
        IReadOnlyCollection<OperationId> allowed,
        ManualResetEventSlim entered,
        ManualResetEventSlim release)
        : IWorkspacePolicyEvaluator
    {
        private readonly HashSet<OperationId> _allowed = [.. allowed];

        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The policy evaluation barrier was not released.");
            }

            return _allowed.Contains(operation.Id) ? PolicyDecision.Allowed : PolicyDecision.Refused;
        }

        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request)
            => PolicyDecision.Refused;

        public PolicyDecision AuthorizeCapability(ActivityContext context, CapabilityDescriptor capability)
            => PolicyDecision.Refused;
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

internal sealed record NestedCatalogResult(NestedCatalogPayload Payload);

internal sealed record NestedCatalogPayload(string Value);

internal sealed record ArrayCatalogResult(string[] Items);

internal sealed record NullableCatalogResult(string? Result);

[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CatalogInput))]
[JsonSerializable(typeof(CatalogResult))]
[JsonSerializable(typeof(NestedCatalogResult))]
[JsonSerializable(typeof(NestedCatalogPayload))]
[JsonSerializable(typeof(ArrayCatalogResult))]
[JsonSerializable(typeof(NullableCatalogResult))]
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

internal sealed class PopulateCollectionInput
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<int> Items { get; } = [7];
}

internal sealed class SetterDisallowsNullInput
{
    [DisallowNull]
    public string? Value { get; set; }
}

internal sealed class SetterAllowsNullResult
{
    [AllowNull]
    public string Value { get; set; } = string.Empty;
}

internal sealed record ReferenceCollectionInput(List<string> Items);

internal sealed record SeededCollectionInput(SeededIntCollection Items);

internal sealed class SeededIntCollection : List<int>
{
    public SeededIntCollection()
    {
        Add(7);
    }
}

internal sealed record CallbackCollectionInput(CallbackIntCollection Items);

internal sealed class CallbackIntCollection : List<int>, IJsonOnDeserialized
{
    public void OnDeserialized() => Add(7);
}

internal sealed record NumberHandledCollectionInput(NumberHandledIntCollection Items);

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
internal sealed class NumberHandledIntCollection : List<int>;

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
[JsonSerializable(typeof(PopulateCollectionInput))]
[JsonSerializable(typeof(SetterDisallowsNullInput))]
[JsonSerializable(typeof(SetterAllowsNullResult))]
[JsonSerializable(typeof(ReferenceCollectionInput))]
[JsonSerializable(typeof(SeededCollectionInput))]
[JsonSerializable(typeof(CallbackCollectionInput))]
[JsonSerializable(typeof(NumberHandledCollectionInput))]
internal sealed partial class HostileJsonSerializerContext : JsonSerializerContext;

internal sealed class MutableGraphInput
{
    public MutableGraphPayload Payload { get; set; } = new();
}

internal sealed class MutableGraphPayload
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class MutableGraphResult
{
    public MutableGraphResult()
    {
    }

    public MutableGraphResult(string result)
    {
        Result = result;
    }

    public string Result { get; set; } = string.Empty;
}

internal sealed class MutableGraphResolver : IJsonTypeInfoResolver
{
    private readonly Dictionary<Type, JsonTypeInfo> _metadata = [];

    public void Add(JsonTypeInfo metadata) => _metadata.Add(metadata.Type, metadata);

    public void Replace(JsonTypeInfo metadata) => _metadata[metadata.Type] = metadata;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        => _metadata.GetValueOrDefault(type);
}
