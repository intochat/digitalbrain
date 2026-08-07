using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Shell;

public sealed partial class ShellModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Shell.ShellModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "ShellModule module",
            Array.Empty<string>(),
            [
                new NeuronCapabilityDescriptor(
                    "flutter.scene",
                    "Shell scene neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "flutter.control-activated",
                            1,
                            "A shell control was activated",
                            CapabilitySchema.For(typeof(ControlActivated)),
                            Array.Empty<string>()),
                    ],
                    Array.Empty<SynapseCapabilityDescriptor>()),
                new NeuronCapabilityDescriptor(
                    "flutter.shell",
                    "Shell neuron",
                    "desk",
                    [
                        new SynapseCapabilityDescriptor(
                            "flutter.open-scene",
                            1,
                            "Open a scene on the shell",
                            CapabilitySchema.For(typeof(OpenScene)),
                            Array.Empty<string>()),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "flutter.scene-opened",
                            1,
                            "A shell scene was opened",
                            CapabilitySchema.For(typeof(SceneOpened)),
                            Array.Empty<string>()),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(ShellModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
