using System.Reflection;
using DigitalBrain;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public sealed class NeuronArchitectureTests
{
    [Fact]
    public void INeuron_declares_zero_methods_and_extends_IGrainWithStringKey()
    {
        Assert.Contains(typeof(IGrainWithStringKey), typeof(INeuron).GetInterfaces());
        var declaredMethods = typeof(INeuron)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Empty(declaredMethods);
    }

    [Fact]
    public void Neuron_derives_from_official_DurableGrain()
    {
        Assert.Equal(typeof(DurableGrain), typeof(Neuron).BaseType);
        Assert.True(typeof(Neuron).IsAbstract);
    }

    [Fact]
    public void NeuronDurableState_has_only_three_durable_members_with_nameof_keys()
    {
        var constructor = typeof(NeuronDurableState).GetConstructors().Single();
        var parameters = constructor.GetParameters();
        Assert.Equal(3, parameters.Length);

        AssertKeyedDurable(
            parameters[0],
            nameof(NeuronDurableState.Status),
            typeof(IDurableValue<NeuronStatus>));
        AssertKeyedDurable(
            parameters[1],
            nameof(NeuronDurableState.Operations),
            typeof(IDurableDictionary<Guid, ExternalOperation>));
        AssertKeyedDurable(
            parameters[2],
            nameof(NeuronDurableState.Outbox),
            typeof(IDurableDictionary<Guid, NeuronNotification>));

        var properties = typeof(NeuronDurableState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(3, properties.Length);
        Assert.Contains(properties, property => property.Name == nameof(NeuronDurableState.Status));
        Assert.Contains(properties, property => property.Name == nameof(NeuronDurableState.Operations));
        Assert.Contains(properties, property => property.Name == nameof(NeuronDurableState.Outbox));
    }

    [Fact]
    public void Neuron_exposes_protected_durable_state_without_public_business_methods()
    {
        var publicMethods = typeof(Neuron)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.Name is not nameof(Grain.OnActivateAsync)
                and not nameof(IRemindable.ReceiveReminder))
            .ToArray();
        Assert.Empty(publicMethods);

        var durableState = typeof(Neuron)
            .GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(property => property.PropertyType == typeof(NeuronDurableState));
        Assert.True(durableState.GetMethod!.IsFamily);
    }

    [Fact]
    public void Neuron_implements_IRemindable_and_has_no_generic_provider_invocation()
    {
        Assert.Contains(typeof(IRemindable), typeof(Neuron).GetInterfaces());

        var publicNames = typeof(Neuron)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Static)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(IRemindable.ReceiveReminder), publicNames);
        Assert.DoesNotContain("Invoke", publicNames);
        Assert.DoesNotContain("Ask", publicNames);
        Assert.DoesNotContain("InvokeMcpTool", publicNames);
        Assert.DoesNotContain("Dispatch", publicNames);
    }

    [Fact]
    public void BrainOwnerId_carries_required_orleans_serialization_metadata()
    {
        Assert.NotNull(typeof(BrainOwnerId).GetCustomAttribute<GenerateSerializerAttribute>());
        var alias = typeof(BrainOwnerId).GetCustomAttribute<AliasAttribute>();
        Assert.NotNull(alias);
        Assert.Equal(nameof(BrainOwnerId), alias.Alias);

        var value = typeof(BrainOwnerId).GetProperty(nameof(BrainOwnerId.Value));
        Assert.NotNull(value);
        var id = value.GetCustomAttribute<IdAttribute>();
        Assert.NotNull(id);
        Assert.Equal(0u, id.Id);
    }

    [Fact]
    public void External_operation_transition_types_are_records_not_string_events()
    {
        Assert.True(typeof(ExternalOperationTransition).IsAbstract);
        Assert.True(typeof(ExternalOperationTransition).IsClass);
        Assert.Contains(
            typeof(ExternalOperationTransition.Succeeded),
            typeof(ExternalOperationTransition).GetNestedTypes());
        Assert.Contains(
            typeof(ExternalOperationTransition.Failed),
            typeof(ExternalOperationTransition).GetNestedTypes());
        Assert.Contains(
            typeof(ExternalOperationTransition.Unknown),
            typeof(ExternalOperationTransition).GetNestedTypes());
        Assert.Contains(
            typeof(ExternalOperationTransition.ReconcileSucceeded),
            typeof(ExternalOperationTransition).GetNestedTypes());
    }

    private static void AssertKeyedDurable(ParameterInfo parameter, string expectedKey, Type expectedType)
    {
        Assert.Equal(expectedType, parameter.ParameterType);
        var keyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
        Assert.NotNull(keyed);
        Assert.Equal(expectedKey, keyed.Key);
    }
}
