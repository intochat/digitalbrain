using System.Collections.Immutable;
using DigitalBrain;
using DigitalBrain.Kernel;
using Orleans.Metadata;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests.Quadrant;

public sealed class QuadrantDiscoveryTests
{
    [Fact]
    public void Public_non_generic_leaf_INeuron_maps_to_exactly_one_Neuron_implementation()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(ILeafNeuron),
            typeof(LeafNeuron),
            typeof(INeuron),
            typeof(Neuron),
        ]);

        var registration = Assert.Single(registrations);
        Assert.Equal(typeof(ILeafNeuron), registration.Contract);
        Assert.Equal(typeof(LeafNeuron), registration.Implementation);

        var quadrant = new DigitalBrain.Kernel.Quadrant();
        quadrant.Load(registrations);

        Assert.Equal(typeof(LeafNeuron), quadrant.GetImplementation<ILeafNeuron>());
        Assert.Equal(typeof(LeafNeuron), quadrant.Neurons[typeof(ILeafNeuron)]);
    }

    [Fact]
    public void Missing_implementation_fails_fast()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NeuronTypeCatalogBuilder.Build([typeof(ILeafNeuron)]));

        Assert.Contains(typeof(ILeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_implementations_fail_fast()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NeuronTypeCatalogBuilder.Build(
            [
                typeof(ILeafNeuron),
                typeof(LeafNeuron),
                typeof(AlternateLeafNeuron),
            ]));

        Assert.Contains(typeof(ILeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(typeof(AlternateLeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(LeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_Neuron_implementation_fails_fast()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NeuronTypeCatalogBuilder.Build(
            [
                typeof(ILeafNeuron),
                typeof(NonNeuronLeaf),
            ]));

        Assert.Contains(typeof(NonNeuronLeaf).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Neuron), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_leaf_interface_fails_fast()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NeuronTypeCatalogBuilder.Build([typeof(IGenericLeafNeuron<>)]));

        Assert.Contains("Generic", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(typeof(IGenericLeafNeuron<>).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_absent_from_Orleans_local_grain_manifest_fails_fast()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(ILeafNeuron),
            typeof(LeafNeuron),
        ]);

        var emptyManifest = new GrainManifest(
            ImmutableDictionary<GrainType, GrainProperties>.Empty,
            ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            OrleansNeuronManifestValidator.Validate(registrations, emptyManifest));

        Assert.Contains(typeof(ILeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(LeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mapping_present_in_Orleans_local_grain_manifest_is_accepted()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(ILeafNeuron),
            typeof(LeafNeuron),
        ]);

        var grainType = GrainType.Create("leaf-neuron");
        var interfaceType = GrainInterfaceType.Create("ileaf-neuron");
        var manifest = CreateManifest(
            grainType,
            interfaceType,
            typeof(LeafNeuron).FullName!,
            typeof(ILeafNeuron).Name);

        OrleansNeuronManifestValidator.Validate(registrations, manifest);
    }

    [Fact]
    public void Base_capability_interface_is_excluded_when_more_derived_INeuron_exists()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(IBaseCapability),
            typeof(IDerivedCapability),
            typeof(DerivedCapabilityNeuron),
        ]);

        var registration = Assert.Single(registrations);
        Assert.Equal(typeof(IDerivedCapability), registration.Contract);
        Assert.Equal(typeof(DerivedCapabilityNeuron), registration.Implementation);
        Assert.DoesNotContain(registrations, item => item.Contract == typeof(IBaseCapability));
    }

    [Fact]
    public void Quadrant_dictionary_is_keyed_by_Type_and_immutable_after_startup()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(ILeafNeuron),
            typeof(LeafNeuron),
        ]);

        var quadrant = new DigitalBrain.Kernel.Quadrant();
        quadrant.Load(registrations);

        Assert.True(quadrant.Neurons.ContainsKey(typeof(ILeafNeuron)));
        Assert.Equal(typeof(LeafNeuron), quadrant.Neurons[typeof(ILeafNeuron)]);
        Assert.IsAssignableFrom<IReadOnlyDictionary<Type, Type>>(quadrant.Neurons);

        Assert.ThrowsAny<NotSupportedException>(() =>
        {
            if (quadrant.Neurons is IDictionary<Type, Type> mutable)
                mutable.Add(typeof(IBaseCapability), typeof(DerivedCapabilityNeuron));
            else if (quadrant.Neurons is ICollection<KeyValuePair<Type, Type>> collection)
                collection.Add(new KeyValuePair<Type, Type>(typeof(IBaseCapability), typeof(DerivedCapabilityNeuron)));
            else
                throw new NotSupportedException("Quadrant neuron map is not a mutable dictionary.");
        });

        Assert.Throws<InvalidOperationException>(() =>
            quadrant.Load(registrations));
    }

    private static GrainManifest CreateManifest(
        GrainType grainType,
        GrainInterfaceType interfaceType,
        string implementationFullName,
        string contractTypeName)
    {
        var grainProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        grainProperties.Add(WellKnownGrainTypeProperties.FullTypeName, implementationFullName);
        grainProperties.Add(
            WellKnownGrainTypeProperties.ImplementedInterfacePrefix + "0",
            interfaceType.ToString()!);

        var interfaceProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.TypeName, contractTypeName);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.DefaultGrainType, grainType.ToString()!);

        return new GrainManifest(
            ImmutableDictionary<GrainType, GrainProperties>.Empty.Add(
                grainType,
                new GrainProperties(grainProperties.ToImmutable())),
            ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty.Add(
                interfaceType,
                new GrainInterfaceProperties(interfaceProperties.ToImmutable())));
    }

    public interface ILeafNeuron : INeuron;

    public sealed class LeafNeuron([NeuronState] NeuronDurableState durableState)
        : Neuron(durableState), ILeafNeuron;

    public sealed class AlternateLeafNeuron([NeuronState] NeuronDurableState durableState)
        : Neuron(durableState), ILeafNeuron;

    public sealed class NonNeuronLeaf : ILeafNeuron;

    public interface IGenericLeafNeuron<T> : INeuron;

    public interface IBaseCapability : INeuron;

    public interface IDerivedCapability : IBaseCapability;

    public sealed class DerivedCapabilityNeuron([NeuronState] NeuronDurableState durableState)
        : Neuron(durableState), IDerivedCapability;
}
