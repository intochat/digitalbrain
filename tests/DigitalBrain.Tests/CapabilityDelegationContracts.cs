using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CapabilityDelegationContracts
{
    [Fact(DisplayName = "the delegation transport is sealed opaque hidden serialized Kernel infrastructure")]
    public void DelegationTransportIsOpaqueKernelInfrastructure()
    {
        var transport = typeof(CapabilityDelegation);

        Assert.True(transport.IsSealed);
        Assert.Equal(
            EditorBrowsableState.Never,
            transport.GetCustomAttribute<EditorBrowsableAttribute>()?.State);
        Assert.Equal(
            "db.capability-delegation",
            transport.GetCustomAttribute<AliasAttribute>()?.Alias);
        Assert.NotNull(transport.GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Empty(transport.GetMembers(
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly));
        Assert.False(typeof(Synapse).IsAssignableFrom(transport));
        Assert.False(typeof(INeuron).IsAssignableFrom(transport));
        Assert.DoesNotContain(
            transport.GetInterfaces(),
            implemented => implemented.Namespace?.StartsWith("Orleans", StringComparison.Ordinal) is true);

        var infrastructureMembers = transport
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["DelegateSource", "Identity", "Owner", "Request"], infrastructureMembers);
    }

    [Fact(DisplayName = "Kernel exposes one capability transport and no delegation service hierarchy")]
    public void KernelExposesOnlyTheApprovedDelegationSurface()
    {
        var kernel = typeof(Neuron).Assembly;
        var capabilityTypes = kernel.GetExportedTypes()
            .Where(type => type.Namespace == typeof(Neuron).Namespace)
            .Where(type => type.Name.Contains("Capability", StringComparison.Ordinal))
            .ToArray();
        var minting = typeof(Neuron).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.IsFamily)
            .Where(method => method.ReturnType == typeof(Task<CapabilityDelegation>))
            .ToArray();
        var carrying = typeof(DigitalBrainRuntime).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(method => method.Name == nameof(DigitalBrainRuntime.InvokeAsync))
            .ToArray();

        Assert.Equal([typeof(CapabilityDelegation)], capabilityTypes);
        Assert.Equal(2, minting.Length);
        Assert.Contains(
            minting,
            method => method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [typeof(GrainId), typeof(NeuronId), typeof(Type), typeof(string)]));
        Assert.Contains(
            minting,
            method => method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [typeof(SynapseId), typeof(GrainId), typeof(NeuronId), typeof(Type), typeof(string)]));
        Assert.Single(carrying);
        Assert.True(carrying[0].IsGenericMethodDefinition);
        Assert.DoesNotContain(
            kernel.GetExportedTypes(),
            type => type.IsInterface
                && (type.Name.Contains("Delegation", StringComparison.Ordinal)
                    || type.Name.Contains("Capability", StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "delegation adds no new friend assembly")]
    public void DelegationAddsNoFriendAssembly()
    {
        var friends = typeof(Neuron).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["DigitalBrain.Testing", "DigitalBrain.Tests"], friends);
    }

    [Theory(DisplayName = "delegation invocation independently rejects inconsistent owner bindings")]
    [InlineData("runner")]
    [InlineData("target")]
    public void DelegationInvocationRejectsInconsistentOwnerBindings(string mismatch)
    {
        var owner = new OwnerId($"delegation-owner-{mismatch}");
        var foreign = new OwnerId($"delegation-owner-{mismatch}-foreign");
        var caller = new NeuronId("issuer", owner, "caller");
        var target = new NeuronId(
            "target",
            mismatch == "target" ? foreign : owner,
            "semantic");
        var runner = GrainId.Create(
            "runner",
            $"{(mismatch == "runner" ? foreign : owner).Value}/delegate");
        var method = typeof(IOwnerBoundCapability).GetMethod(
            nameof(IOwnerBoundCapability.InvokeAsync))!;
        var request = SynapseDelivery.Create(
            new CapabilityRequested(method.DeclaringType!.FullName!, method.Name, target),
            caller,
            sequence: 1);
        var delegation = new CapabilityDelegation(Guid.NewGuid(), request, runner, owner);

        Assert.Throws<NeuronAuthorizationException>(
            () => delegation.RequireMatches(runner, target.ToGrainId(), method));
    }

    [Fact(DisplayName = "the public delegation helper restores nested ambient presentation")]
    public async Task PublicDelegationHelperRestoresNestedAmbientPresentation()
    {
        var outer = CreateDelegation("helper-outer");
        var inner = CreateDelegation("helper-inner");

        Assert.Null(CapabilityRequestContext.CurrentDelegation);

        var result = await DigitalBrainRuntime.InvokeAsync(
            outer,
            async () =>
            {
                Assert.Same(outer, CapabilityRequestContext.CurrentDelegation);

                var nested = await DigitalBrainRuntime.InvokeAsync(
                    inner,
                    () => Task.FromResult(17));

                Assert.Same(outer, CapabilityRequestContext.CurrentDelegation);

                return nested;
            });

        Assert.Equal(17, result);
        Assert.Null(CapabilityRequestContext.CurrentDelegation);
    }

    [Fact(DisplayName = "the public delegation helper clears ambient presentation after exceptions")]
    public async Task PublicDelegationHelperClearsAmbientPresentationAfterException()
    {
        var delegation = CreateDelegation("helper-exception");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DigitalBrainRuntime.InvokeAsync<int>(
                delegation,
                () => throw new InvalidOperationException("expected helper failure")));

        Assert.Null(CapabilityRequestContext.CurrentDelegation);
    }

    [Fact(DisplayName = "redeemed RequestContext presentation restores nested ambient state")]
    public async Task RedeemedPresentationRestoresNestedAmbientState()
    {
        RequestContext.Clear();

        var outer = CreateDelegation("redeemed-outer");
        var inner = CreateDelegation("redeemed-inner");

        await CapabilityRequestContext.InvokeRedeemedAsync(
            outer,
            async () =>
            {
                Assert.Same(
                    outer,
                    CapabilityRequestContext.CurrentRedeemedDelegation?.Delegation);

                await CapabilityRequestContext.InvokeRedeemedAsync(
                    inner,
                    () =>
                    {
                        Assert.Same(
                            inner,
                            CapabilityRequestContext.CurrentRedeemedDelegation?.Delegation);

                        return Task.CompletedTask;
                    });

                Assert.Same(
                    outer,
                    CapabilityRequestContext.CurrentRedeemedDelegation?.Delegation);
            });

        Assert.Null(CapabilityRequestContext.CurrentRedeemedDelegation);
        Assert.Null(CapabilityRequestContext.CurrentDelivery);
    }

    [Fact(DisplayName = "redeemed RequestContext presentation restores a prior delivery after exceptions")]
    public async Task RedeemedPresentationRestoresPriorDeliveryAfterException()
    {
        RequestContext.Clear();

        var delegation = CreateDelegation("redeemed-exception");
        var prior = CreateDelegation("redeemed-prior").Request;

        await CapabilityRequestContext.InvokeAsync(
            prior,
            async () =>
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => CapabilityRequestContext.InvokeRedeemedAsync(
                        delegation,
                        () => throw new InvalidOperationException("expected redeemed failure")));

                Assert.Same(prior, CapabilityRequestContext.CurrentDelivery);
                Assert.Null(CapabilityRequestContext.CurrentRedeemedDelegation);
            });

        Assert.Null(CapabilityRequestContext.CurrentDelivery);
        Assert.Null(CapabilityRequestContext.CurrentRedeemedDelegation);
    }

    private static CapabilityDelegation CreateDelegation(string name)
    {
        var owner = new OwnerId(name);
        var caller = new NeuronId("issuer", owner, "caller");
        var target = new NeuronId("target", owner, "target");
        var request = SynapseDelivery.Create(
            new CapabilityRequested(
                typeof(IOwnerBoundCapability).FullName!,
                nameof(IOwnerBoundCapability.InvokeAsync),
                target),
            caller,
            sequence: 1);

        return new CapabilityDelegation(
            Guid.NewGuid(),
            request,
            GrainId.Create("runner", $"{owner.Value}/delegate"),
            owner);
    }

}

internal interface IOwnerBoundCapability : INeuron
{
    Task InvokeAsync();
}
