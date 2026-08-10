using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Google;

public sealed partial class GoogleModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Google.GoogleModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "GoogleModule module",
            [
                new NeuronCapabilityDescriptor(
                    "DigitalBrain.Google.IGmail",
                    "Owner-scoped Gmail neuron identified by module-owned connection name",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-get-message-request",
                            1,
                            "Read-only fetch of one Gmail message by id; body is bounded by the handler",
                            CapabilitySchema.For(typeof(GmailGetMessageRequest))),
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-request",
                            1,
                            "Intent-level Gmail request; provider tools stay inside GoogleModule",
                            CapabilitySchema.For(typeof(GmailRequest))),
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-search-request",
                            1,
                            "Read-only Gmail search by query syntax; MaxResults is 1..10",
                            CapabilitySchema.For(typeof(GmailSearchRequest))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-get-message-response",
                            1,
                            "Bounded read-only Gmail message fetch result",
                            CapabilitySchema.For(typeof(GmailGetMessageResponse))),
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-response",
                            1,
                            "Bounded typed Gmail result for an intent request",
                            CapabilitySchema.For(typeof(GmailResponse))),
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-search-response",
                            1,
                            "Bounded read-only Gmail search headers (metadata only, no body)",
                            CapabilitySchema.For(typeof(GmailSearchResponse))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(GoogleModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
