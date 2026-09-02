using System.Reflection;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using Orleans;
using Orleans.Concurrency;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class ContractShapeTests
{
    [Fact]
    public void Neuron_RequiresRuntimeCompositionBoundary()
    {
        _ = Assert.Single(
            typeof(Neuron).GetMembers(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            static candidate => candidate is { MemberType: MemberTypes.Constructor, Name: ".ctor" });
        var constructor = typeof(Neuron).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(NeuronRuntime)],
            modifiers: null);

        Assert.NotNull(constructor);
        Assert.True(constructor.IsFamily);
        Assert.Null(typeof(Neuron).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null));
    }

    [Fact]
    public void NeuronContractsSeparateDeliveryFromObservation()
    {
        Assert.Equal(
            [nameof(INeuron.Deliver)],
            typeof(INeuron).GetMethods().Select(static method => method.Name).Order().ToArray());
        Assert.Equal(
            typeof(Task<DeliveryOutcome>),
            typeof(INeuron).GetMethod(nameof(INeuron.Deliver))!.ReturnType);
        Assert.Equal(
            [
                nameof(INeuronQuery.ReadJournal),
                nameof(INeuronQuery.ReadSynapses),
                nameof(INeuronQuery.Unwatch),
                nameof(INeuronQuery.Watch),
            ],
            typeof(INeuronQuery).GetMethods().Select(static method => method.Name).Order().ToArray());
    }

    [Fact]
    public void DeliveryOutcomeIsAnAliasedWireType()
    {
        Assert.NotNull(typeof(DeliveryOutcome).GetCustomAttribute<GenerateSerializerAttribute>());
        var alias = Assert.Single(typeof(DeliveryOutcome).GetCustomAttributes<AliasAttribute>());
        Assert.Equal("db.v2.delivery-outcome", alias.Alias);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(DeliveryOutcome)));
        Assert.Equal((byte)0, (byte)DeliveryOutcome.Handled);
        Assert.Equal((byte)1, (byte)DeliveryOutcome.Unhandled);
        Assert.Equal((byte)2, (byte)DeliveryOutcome.Refused);
    }

    [Fact]
    public void NeuronPortsUseTheIntentionalV2Aliases()
    {
        Assert.Equal(
            "db.v2.neuron",
            Assert.Single(typeof(INeuron).GetCustomAttributes<AliasAttribute>()).Alias);
        Assert.Equal(
            "db.v2.neuron-query",
            Assert.Single(typeof(INeuronQuery).GetCustomAttributes<AliasAttribute>()).Alias);
    }

    [Fact]
    public void SignalDeliveryFactoryRequiresTheClockBeforeOptionalLineage()
    {
        var create = Assert.Single(
            typeof(SignalDelivery).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name == nameof(SignalDelivery.Create));
        var parameters = create.GetParameters();

        Assert.Equal(
            [
                typeof(Signal),
                typeof(NeuronId),
                typeof(long),
                typeof(TimeProvider),
                typeof(SignalDelivery),
                typeof(CorrelationId),
                typeof(PrincipalId),
            ],
            parameters.Select(static parameter => Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType));
        Assert.False(parameters[3].IsOptional);
        Assert.All(parameters[4..], static parameter => Assert.True(parameter.IsOptional));
    }

    [Fact]
    public void SignalDeliveryFactoryRejectsANullClock()
    {
        var caller = new NeuronId("caller", new OwnerId("owner"), "source");

        Assert.Throws<ArgumentNullException>(
            () => SignalDelivery.Create(new Ping("ping"), caller, 1, null!));
    }

    [Fact]
    public void QueryReadsInterleaveWhileObserverManagementRemainsSerialized()
    {
        AssertInterleavable(nameof(INeuronQuery.ReadJournal));
        AssertInterleavable(nameof(INeuronQuery.ReadSynapses));
        AssertSerialized(nameof(INeuronQuery.Watch));
        AssertSerialized(nameof(INeuronQuery.Unwatch));
    }

    private static void AssertInterleavable(string methodName)
    {
        var method = typeof(INeuronQuery).GetMethod(methodName)!;
        Assert.NotNull(method.GetCustomAttribute<ReadOnlyAttribute>());
        Assert.NotNull(method.GetCustomAttribute<AlwaysInterleaveAttribute>());
    }

    private static void AssertSerialized(string methodName)
    {
        var method = typeof(INeuronQuery).GetMethod(methodName)!;
        Assert.Null(method.GetCustomAttribute<ReadOnlyAttribute>());
        Assert.Null(method.GetCustomAttribute<AlwaysInterleaveAttribute>());
    }
}
