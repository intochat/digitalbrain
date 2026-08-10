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
                            "ui.note",
                            1,
                            "Post a line of text into a chat transcript",
                            CapabilitySchema.For(typeof(Note))),
                        new SynapseCapabilityDescriptor(
                            "ui.timer-card",
                            1,
                            "Post a countdown clock card into a chat transcript",
                            CapabilitySchema.For(typeof(TimerCard))),
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
                    "ui.chart",
                    "Chart control with identity",
                    "dashboard",
                    [
                        new SynapseCapabilityDescriptor(
                            ChartPoint.AliasName,
                            1,
                            "Append one point to whatever chart receives it",
                            CapabilitySchema.For(typeof(ChartPoint))),
                    ],
                    []),
                new NeuronCapabilityDescriptor(
                    "ui.diagram",
                    "Diagram control with identity",
                    "main",
                    [
                        new SynapseCapabilityDescriptor(
                            Node.AliasName,
                            1,
                            "Place or update one node on whatever diagram receives it",
                            CapabilitySchema.For(typeof(Node))),
                        new SynapseCapabilityDescriptor(
                            Edge.AliasName,
                            1,
                            "Draw or update one directed edge on whatever diagram receives it",
                            CapabilitySchema.For(typeof(Edge))),
                    ],
                    []),
                new NeuronCapabilityDescriptor(
                    "ui.button",
                    "Interactive button control",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "ui.button-clicked",
                            1,
                            "Owner activated a button offered in a chat turn",
                            CapabilitySchema.For(typeof(ButtonClicked))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            ButtonActivated.AliasName,
                            1,
                            "A button neuron was activated by its owner",
                            CapabilitySchema.For(typeof(ButtonActivated))),
                    ]),
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
