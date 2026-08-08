using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AccountEnrichment;

public sealed partial class EnrichmentModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.AccountEnrichment.EnrichmentModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "EnrichmentModule module",
            [
                new NeuronCapabilityDescriptor(
                    "account-enrichment",
                    "Sample account enrichment neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "db.account-enrichment.requested",
                            1,
                            "Request account enrichment from an email",
                            CapabilitySchema.For(typeof(EnrichAccountFromEmail))),
                        new SynapseCapabilityDescriptor(
                            "db.google.gmail-response",
                            1,
                            "Bounded typed Gmail result for an intent request",
                            CapabilitySchema.For(typeof(DigitalBrain.Google.GmailResponse))),
                        new SynapseCapabilityDescriptor(
                            "db.salesforce.mutation-approval",
                            1,
                            "Approved Salesforce mutation",
                            CapabilitySchema.For(typeof(DigitalBrain.Salesforce.SalesforceMutationApproval))),
                        new SynapseCapabilityDescriptor(
                            "db.salesforce.response",
                            1,
                            "Bounded typed Salesforce result for an intent or approval request",
                            CapabilitySchema.For(typeof(DigitalBrain.Salesforce.SalesforceResponse))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "db.account-enrichment.completed",
                            1,
                            "Account enrichment completed",
                            CapabilitySchema.For(typeof(AccountEnriched))),
                        new SynapseCapabilityDescriptor(
                            "db.account-enrichment.proposed",
                            1,
                            "Account enrichment was proposed",
                            CapabilitySchema.For(typeof(AccountEnrichmentProposed))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(EnrichmentModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
