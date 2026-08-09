using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.UI;

public sealed partial class UiModule : ICompiledModule
{
    public static ModuleId Id { get; } = new("DigitalBrain.UI.UiModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "UI module",
            [
                new NeuronCapabilityDescriptor(
                    "chat",
                    "Owner conversation neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "chat.read-transcript-request",
                            1,
                            "Returns the durable transcript kept for one named conversation",
                            CapabilitySchema.For(typeof(ReadTranscriptRequest))),
                        new SynapseCapabilityDescriptor(
                            "ui.button-clicked",
                            1,
                            "Owner activated a button offered in a chat turn",
                            CapabilitySchema.For(typeof(ButtonClicked))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "chat.responded",
                            1,
                            "A response committed into a chat transcript",
                            CapabilitySchema.For(typeof(Responded))),
                        new SynapseCapabilityDescriptor(
                            "chat.transcript-read",
                            1,
                            "A conversation's durable transcript",
                            CapabilitySchema.For(typeof(TranscriptRead))),
                        new SynapseCapabilityDescriptor(
                            "chat.user-messaged",
                            1,
                            "User message accepted into a chat transcript",
                            CapabilitySchema.For(typeof(UserMessaged))),
                    ]),
                new NeuronCapabilityDescriptor(
                    "ui.surface",
                    "Owner UI surface",
                    "desk",
                    [
                        new SynapseCapabilityDescriptor(
                            "ui.open-surface",
                            1,
                            "Open content on a UI surface",
                            CapabilitySchema.For(typeof(OpenSurface))),
                        new SynapseCapabilityDescriptor(
                            "ui.control-activated",
                            1,
                            "A surface control was activated",
                            CapabilitySchema.For(typeof(ControlActivated))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "ui.surface-opened",
                            1,
                            "Content was opened on a UI surface",
                            CapabilitySchema.For(typeof(SurfaceOpened))),
                    ]),
                new NeuronCapabilityDescriptor(
                    "ui.button",
                    "Interactive button control",
                    "default",
                    [],
                    []),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(UiModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
