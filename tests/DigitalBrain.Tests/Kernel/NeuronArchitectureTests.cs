using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
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
            .ToArray();
        Assert.Empty(publicMethods);

        var durableState = typeof(Neuron)
            .GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(property => property.PropertyType == typeof(NeuronDurableState));
        Assert.True(durableState.GetMethod!.IsFamily);
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

    private static void AssertKeyedDurable(ParameterInfo parameter, string expectedKey, Type expectedType)
    {
        Assert.Equal(expectedType, parameter.ParameterType);
        var keyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
        Assert.NotNull(keyed);
        Assert.Equal(expectedKey, keyed.Key);
    }
}
