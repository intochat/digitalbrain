using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Chat;

public sealed partial class ChatModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Chat.ChatModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "ChatModule module",
            [
                new NeuronCapabilityDescriptor(
                    "chat",
                    "Owner conversation neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "chat.read-transcript-request",
                            1,
                            "Returns the durable transcript kept for one named conversation, optionally narrowed to recent entries",
                            CapabilitySchema.For(typeof(ReadTranscriptRequest))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "chat.assistant-responded",
                            1,
                            "Assistant response committed into a chat transcript",
                            CapabilitySchema.For(typeof(AssistantResponded))),
                        new SynapseCapabilityDescriptor(
                            "chat.transcript-read",
                            1,
                            "A conversation's durable transcript, answering a chat.read-transcript-request",
                            CapabilitySchema.For(typeof(TranscriptRead))),
                        new SynapseCapabilityDescriptor(
                            "chat.user-messaged",
                            1,
                            "User message accepted into a chat transcript",
                            CapabilitySchema.For(typeof(UserMessaged))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(ChatModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
