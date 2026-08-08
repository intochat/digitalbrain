using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Salesforce;

public sealed partial class SalesforceModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Salesforce.SalesforceModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "SalesforceModule module",
            [
                new NeuronCapabilityDescriptor(
                    "DigitalBrain.Salesforce.ISalesforce",
                    "Owner-scoped Salesforce neuron identified by module-owned connection name",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "db.salesforce.approve-mutation",
                            1,
                            "Session-owned request to execute a previously proposed Salesforce mutation",
                            CapabilitySchema.For(typeof(ApproveSalesforceMutation))),
                        new SynapseCapabilityDescriptor(
                            "db.salesforce.request",
                            1,
                            "Intent-level Salesforce request; provider tools stay inside SalesforceModule",
                            CapabilitySchema.For(typeof(SalesforceRequest))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "db.salesforce.response",
                            1,
                            "Bounded typed Salesforce result for an intent or approval request",
                            CapabilitySchema.For(typeof(SalesforceResponse))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(SalesforceModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
