using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.TestingTests.Harness;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ActiveCapabilityCatalogTests
{
    [Fact(DisplayName = "active catalog includes only selected module capabilities")]
    public void SelectedModulesAreActive()
    {
        var greeter = (ICompiledModule)new GreeterModule();
        var probes = (ICompiledModule)new CapabilityProbeModule();
        var catalog = ActiveCapabilityCatalog.Create([greeter]);

        Assert.True(catalog.TryGetModule(greeter.Id, out var selected));
        Assert.NotNull(selected);
        Assert.False(catalog.TryGetModule(probes.Id, out _));
        Assert.DoesNotContain(catalog.Modules, module => module.ModuleId == probes.Id);
        Assert.Single(catalog.Modules);

        Assert.True(catalog.TryGetNeuron("harness.greeter", out _));
        Assert.True(catalog.TryGetSynapse("harness.say-hello", schemaVersion: 1, out _));
        Assert.False(catalog.TryGetNeuron("testing.capability-caller", out _));
        Assert.False(catalog.TryGetNeuron("testing.capability-target", out _));
        Assert.False(catalog.TryGetSynapse("db.testing.capability-ping", schemaVersion: 1, out _));
    }

    [Fact(DisplayName = "active catalog indexes neurons and synapses by stable ids and schema version")]
    public void ExactLookupByStableIds()
    {
        var greeter = (ICompiledModule)new GreeterModule();
        var catalog = ActiveCapabilityCatalog.Create([greeter]);

        Assert.True(catalog.TryGetModule(greeter.Id, out var module));
        Assert.Equal("1.0.0", module!.Version);
        Assert.Equal(greeter.Id, module.ModuleId);

        Assert.True(catalog.TryGetNeuron("harness.greeter", out var neuron));
        Assert.Equal("default", neuron!.DefaultInstanceName);

        Assert.True(catalog.TryGetSynapse("harness.say-hello", schemaVersion: 1, out var accepted));
        Assert.True(catalog.TryGetSynapse("harness.greeted", schemaVersion: 1, out var emitted));
        Assert.False(string.IsNullOrWhiteSpace(accepted!.JsonSchema));
        Assert.False(string.IsNullOrWhiteSpace(emitted!.JsonSchema));
        Assert.DoesNotContain("Version=", accepted.JsonSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("Culture=", accepted.JsonSchema, StringComparison.Ordinal);
        Assert.DoesNotContain(", ", accepted.JsonSchema.Split('"')[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "duplicate active module ids fail catalog construction with a precise error")]
    public void DuplicateModuleIdsFail()
    {
        var greeter = (ICompiledModule)new GreeterModule();
        var failure = Assert.Throws<InvalidOperationException>(
            () => ActiveCapabilityCatalog.Create([greeter, greeter]));

        Assert.Contains(greeter.Id.Value, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Duplicate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "duplicate active neuron contract ids fail catalog construction naming both modules")]
    public void DuplicateNeuronIdsFail()
    {
        var left = new ScriptedModule(
            new ModuleId("catalog.left"),
            Manifest(
                "catalog.left",
                new NeuronCapabilityDescriptor(
                    "catalog.shared-neuron",
                    "left neuron",
                    "default",
                    [],
                    [])));
        var right = new ScriptedModule(
            new ModuleId("catalog.right"),
            Manifest(
                "catalog.right",
                new NeuronCapabilityDescriptor(
                    "catalog.shared-neuron",
                    "right neuron",
                    "default",
                    [],
                    [])));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ActiveCapabilityCatalog.Create([left, right]));

        Assert.Contains("catalog.shared-neuron", failure.Message, StringComparison.Ordinal);
        Assert.Contains("catalog.left", failure.Message, StringComparison.Ordinal);
        Assert.Contains("catalog.right", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Duplicate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "incompatible schemas for the same synapse id and version fail catalog construction")]
    public void IncompatibleSchemasFail()
    {
        var left = new ScriptedModule(
            new ModuleId("catalog.left"),
            Manifest(
                "catalog.left",
                new NeuronCapabilityDescriptor(
                    "catalog.neuron",
                    "neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "catalog.synapse",
                            1,
                            "left",
                            """{"type":"object","title":"left"}""",
                            []),
                    ],
                    [])));
        var right = new ScriptedModule(
            new ModuleId("catalog.right"),
            Manifest(
                "catalog.right",
                new NeuronCapabilityDescriptor(
                    "catalog.other-neuron",
                    "other",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "catalog.synapse",
                            1,
                            "right",
                            """{"type":"object","title":"right"}""",
                            []),
                    ],
                    [])));

        var failure = Assert.Throws<InvalidOperationException>(
            () => ActiveCapabilityCatalog.Create([left, right]));

        Assert.Contains("catalog.synapse", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Incompatible", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "catalog construction is deterministic for the same selected module set")]
    public void CatalogIsDeterministic()
    {
        var alpha = new ScriptedModule(
            new ModuleId("catalog.alpha"),
            Manifest("catalog.alpha"));
        var beta = new ScriptedModule(
            new ModuleId("catalog.beta"),
            Manifest("catalog.beta"));

        var first = ActiveCapabilityCatalog.Create([beta, alpha]);
        var second = ActiveCapabilityCatalog.Create([alpha, beta]);

        Assert.Equal(
            first.Modules.Select(module => module.ModuleId.Value),
            second.Modules.Select(module => module.ModuleId.Value));
        Assert.Equal(["catalog.alpha", "catalog.beta"], first.Modules.Select(module => module.ModuleId.Value));
    }

    private static CapabilityManifest Manifest(string moduleId, params NeuronCapabilityDescriptor[] neurons)
        => new(new ModuleId(moduleId), "1.0.0", moduleId, [], neurons);

    private sealed class ScriptedModule(ModuleId id, CapabilityManifest capabilities) : ICompiledModule
    {
        public ModuleId Id { get; } = id;

        public CapabilityManifest Capabilities { get; } = capabilities;

        public void PrepareSerialization(IServiceCollection services)
        {
        }

        public void Activate(ISiloBuilder builder)
        {
        }
    }
}
