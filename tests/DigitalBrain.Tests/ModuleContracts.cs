using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleContracts
{
    [Fact(DisplayName = "a module descriptor carries configuration, secrets, capabilities, effects and connections — not neurons or synapses")]
    public void DescriptorDeclaresNoWiringGraph()
    {
        var names = typeof(ModuleDescriptor).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(ModuleDescriptor.Capabilities),
                nameof(ModuleDescriptor.Configuration),
                nameof(ModuleDescriptor.Connections),
                nameof(ModuleDescriptor.DisplayName),
                nameof(ModuleDescriptor.Effects),
                nameof(ModuleDescriptor.Id),
                nameof(ModuleDescriptor.Secrets),
                nameof(ModuleDescriptor.Version),
            ],
            names);

        Assert.DoesNotContain(names, name => name.Contains("Neuron", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Synapse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "composition rejects a module whose declarations are inconsistent at Build, not at first use")]
    public void InconsistentModuleIsRejectedAtComposition()
    {
        var failure = Assert.Throws<ModuleCompositionException>(() =>
            ModuleComposition.Validate(new InconsistentModule()));

        Assert.Contains(nameof(InconsistentModule), failure.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate capability", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InconsistentModule : IModule
    {
        public ModuleDescriptor Descriptor { get; } = new(
            Id: "inconsistent",
            Version: "1.0.0",
            DisplayName: "Inconsistent",
            Configuration: [],
            Secrets: [],
            Capabilities:
            [
                new CapabilityDeclaration("mail.send", "Send mail"),
                new CapabilityDeclaration("mail.send", "Also send mail"),
            ],
            Effects: [],
            Connections: []);
    }
}
