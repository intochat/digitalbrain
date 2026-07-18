using System.Collections.Immutable;
using DigitalBrain;
using DigitalBrain.Kernel;
using Orleans.Metadata;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests.Quadrant;

public sealed class QuadrantStartupTests
{
    [Fact]
    public void AddBrainKernel_registers_Quadrant_singleton_and_startup_task()
    {
        var hostingSource = ReadKernelSource("KernelHosting.cs");
        Assert.Contains("AddSingleton<Quadrant>()", hostingSource, StringComparison.Ordinal);
        Assert.Contains("AddStartupTask<QuadrantStartupTask>()", hostingSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_task_loads_quadrant_from_explicit_types_and_validates_manifest()
    {
        var quadrant = new DigitalBrain.Kernel.Quadrant();
        var grainType = GrainType.Create("startup-leaf");
        var interfaceType = GrainInterfaceType.Create("istartup-leaf");
        var manifest = CreateManifest(
            grainType,
            interfaceType,
            typeof(StartupLeafNeuron).FullName!,
            typeof(IStartupLeafNeuron).Name);

        var task = new QuadrantStartupTask(
            quadrant,
            new FixedClusterManifestProvider(manifest),
            () =>
            [
                typeof(IStartupLeafNeuron),
                typeof(StartupLeafNeuron),
            ]);

        await task.Execute(CancellationToken.None);

        Assert.Equal(typeof(StartupLeafNeuron), quadrant.GetImplementation<IStartupLeafNeuron>());
    }

    [Fact]
    public async Task Startup_task_lets_validation_exceptions_escape()
    {
        var quadrant = new DigitalBrain.Kernel.Quadrant();
        var emptyManifest = new GrainManifest(
            ImmutableDictionary<GrainType, GrainProperties>.Empty,
            ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty);

        var task = new QuadrantStartupTask(
            quadrant,
            new FixedClusterManifestProvider(emptyManifest),
            () =>
            [
                typeof(IStartupLeafNeuron),
                typeof(StartupLeafNeuron),
            ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            task.Execute(CancellationToken.None));

        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_and_builder_use_Orleans_well_known_property_constants()
    {
        var validatorSource = ReadKernelSource("OrleansNeuronManifestValidator.cs");
        Assert.Contains(nameof(WellKnownGrainInterfaceProperties.TypeName), validatorSource, StringComparison.Ordinal);
        Assert.Contains(nameof(WellKnownGrainInterfaceProperties.DefaultGrainType), validatorSource, StringComparison.Ordinal);
        Assert.Contains(nameof(WellKnownGrainTypeProperties.FullTypeName), validatorSource, StringComparison.Ordinal);
        Assert.Contains(nameof(WellKnownGrainTypeProperties.ImplementedInterfacePrefix), validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"type-name\"", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"primary-grain-type\"", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"full-type-name\"", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"interface.\"", validatorSource, StringComparison.Ordinal);

        var builderSource = ReadKernelSource("NeuronTypeCatalogBuilder.cs");
        Assert.DoesNotContain("AppDomain", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAssemblies", builderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_accepts_real_local_manifest_shape_with_TypeName_and_implemented_interface_without_DefaultGrainType()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(IStartupLeafNeuron),
            typeof(StartupLeafNeuron),
        ]);

        var grainType = GrainType.Create("startup-leaf");
        var interfaceType = GrainInterfaceType.Create(typeof(IStartupLeafNeuron).FullName!);
        var grainProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        grainProperties.Add(WellKnownGrainTypeProperties.FullTypeName, typeof(StartupLeafNeuron).FullName!);
        grainProperties.Add(
            WellKnownGrainTypeProperties.ImplementedInterfacePrefix + "0",
            interfaceType.ToString()!);

        var interfaceProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.TypeName, typeof(IStartupLeafNeuron).Name);

        var manifest = new GrainManifest(
            ImmutableDictionary<GrainType, GrainProperties>.Empty.Add(
                grainType,
                new GrainProperties(grainProperties.ToImmutable())),
            ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty.Add(
                interfaceType,
                new GrainInterfaceProperties(interfaceProperties.ToImmutable())));

        OrleansNeuronManifestValidator.Validate(registrations, manifest);
    }

    [Fact]
    public void Validator_rejects_mapping_when_neither_DefaultGrainType_nor_implemented_interface_matches()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(IStartupLeafNeuron),
            typeof(StartupLeafNeuron),
        ]);

        var grainType = GrainType.Create("startup-leaf");
        var interfaceType = GrainInterfaceType.Create(typeof(IStartupLeafNeuron).FullName!);
        var grainProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        grainProperties.Add(WellKnownGrainTypeProperties.FullTypeName, typeof(StartupLeafNeuron).FullName!);
        grainProperties.Add(
            WellKnownGrainTypeProperties.ImplementedInterfacePrefix + "0",
            "unrelated.interface");

        var interfaceProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.TypeName, typeof(IStartupLeafNeuron).Name);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.DefaultGrainType, "other-grain");

        var manifest = new GrainManifest(
            ImmutableDictionary<GrainType, GrainProperties>.Empty.Add(
                grainType,
                new GrainProperties(grainProperties.ToImmutable())),
            ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty.Add(
                interfaceType,
                new GrainInterfaceProperties(interfaceProperties.ToImmutable())));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            OrleansNeuronManifestValidator.Validate(registrations, manifest));

        Assert.Contains(typeof(IStartupLeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(StartupLeafNeuron).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_accepts_implemented_interface_match_even_when_DefaultGrainType_mismatches()
    {
        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(IStartupLeafNeuron),
            typeof(StartupLeafNeuron),
        ]);

        var grainType = GrainType.Create("startup-leaf");
        var interfaceType = GrainInterfaceType.Create(typeof(IStartupLeafNeuron).FullName!);
        var grainProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        grainProperties.Add(WellKnownGrainTypeProperties.FullTypeName, typeof(StartupLeafNeuron).FullName!);
        grainProperties.Add(
            WellKnownGrainTypeProperties.ImplementedInterfacePrefix + "0",
            interfaceType.ToString()!);

        var interfaceProperties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.TypeName, typeof(IStartupLeafNeuron).Name);
        interfaceProperties.Add(WellKnownGrainInterfaceProperties.DefaultGrainType, "other-grain");

        var manifest = new GrainManifest(
            ImmutableDictionary<GrainType, GrainProperties>.Empty.Add(
                grainType,
                new GrainProperties(grainProperties.ToImmutable())),
            ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty.Add(
                interfaceType,
                new GrainInterfaceProperties(interfaceProperties.ToImmutable())));

        OrleansNeuronManifestValidator.Validate(registrations, manifest);
    }

    private static string ReadKernelSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "kernel", "DigitalBrain.Kernel", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(fileName);
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

    public interface IStartupLeafNeuron : INeuron;

    public sealed class StartupLeafNeuron([NeuronState] NeuronDurableState durableState)
        : Neuron(durableState), IStartupLeafNeuron;

    private sealed class FixedClusterManifestProvider(GrainManifest local) : IClusterManifestProvider
    {
        public GrainManifest LocalGrainManifest { get; } = local;

        public ClusterManifest Current =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ClusterManifest> Updates =>
            throw new NotSupportedException();
    }
}
