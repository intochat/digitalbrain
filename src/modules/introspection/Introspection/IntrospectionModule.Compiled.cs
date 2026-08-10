using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Introspection;

public sealed partial class IntrospectionModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Introspection.IntrospectionModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "IntrospectionModule module",
            [
                new NeuronCapabilityDescriptor(
                    "introspection",
                    "Introspection neuron reporting journal tallies, journaled facts and runtime topology for the owning identity",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "introspection.read-journal-request",
                            1,
                            "Returns journaled causal facts for one neuron of the owning identity, bounded by cursor and limit; entries record synapse kinds and lineage, excluding argument and payload values",
                            CapabilitySchema.For(typeof(ReadJournalRequest))),
                        new SynapseCapabilityDescriptor(
                            "introspection.read-topology-request",
                            1,
                            "Reports the runtime topology of the owning identity: modules the deployment composed and neurons currently activated",
                            CapabilitySchema.For(typeof(ReadTopologyRequest))),
                        new SynapseCapabilityDescriptor(
                            "introspection.tally-journal-request",
                            1,
                            "Counts journaled synapses by synapse kinds for one neuron of the owning identity, answering how often a conversation recorded owner messages",
                            CapabilitySchema.For(typeof(TallyJournalRequest))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "introspection.journal-page-read",
                            1,
                            "A page of causal facts from a neuron journal, or why the read was refused",
                            CapabilitySchema.For(typeof(JournalPageRead))),
                        new SynapseCapabilityDescriptor(
                            "introspection.journal-tallied",
                            1,
                            "How many synapses of each type a neuron journal has recorded, or why the tally was refused",
                            CapabilitySchema.For(typeof(JournalTallied))),
                        new SynapseCapabilityDescriptor(
                            "introspection.topology-read",
                            1,
                            "The modules this deployment composed and the owner's currently activated neurons",
                            CapabilitySchema.For(typeof(TopologyRead))),
                    ]),
                new NeuronCapabilityDescriptor(
                    "db.synapse-graph",
                    "Owner synapse graph: durable runtime routes between neuron instances",
                    ISynapseGraph.InstanceName,
                    [
                        new SynapseCapabilityDescriptor(
                            "db.connect",
                            1,
                            "Create or replace a synapse route: deliver a source neuron's emitted synapses to a target neuron, optionally through a named transform, optionally until an expiry",
                            CapabilitySchema.For(typeof(Connect))),
                        new SynapseCapabilityDescriptor(
                            "db.disconnect",
                            1,
                            "Remove a synapse connection by its identity",
                            CapabilitySchema.For(typeof(Disconnect))),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "db.connected",
                            1,
                            "A synapse route is live",
                            CapabilitySchema.For(typeof(Connected))),
                        new SynapseCapabilityDescriptor(
                            "db.disconnected",
                            1,
                            "A synapse route was removed",
                            CapabilitySchema.For(typeof(Disconnected))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(IntrospectionModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
