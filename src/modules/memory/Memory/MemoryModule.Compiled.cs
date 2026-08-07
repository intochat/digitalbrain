using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory;

public sealed partial class MemoryModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Memory.MemoryModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "MemoryModule module",
            Array.Empty<string>(),
            [
                new NeuronCapabilityDescriptor(
                    "DigitalBrain.Memory.IVectorMemory",
                    "Owner-isolated vector memory neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "memory.remove-vector",
                            1,
                            "Remove a vector memory entry by key",
                            CapabilitySchema.For(typeof(RemoveVectorMemory)),
                            Array.Empty<string>()),
                        new SynapseCapabilityDescriptor(
                            "memory.search-vector",
                            1,
                            "Search vector memory by semantic similarity",
                            CapabilitySchema.For(typeof(SearchVectorMemory)),
                            Array.Empty<string>()),
                        new SynapseCapabilityDescriptor(
                            "memory.store-vector",
                            1,
                            "Store a text entry in vector memory",
                            CapabilitySchema.For(typeof(StoreVectorMemory)),
                            Array.Empty<string>()),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "memory.vector-matches",
                            1,
                            "Ordered vector memory search results",
                            CapabilitySchema.For(typeof(VectorMemoryMatches)),
                            Array.Empty<string>()),
                        new SynapseCapabilityDescriptor(
                            "memory.vector-removed",
                            1,
                            "Result of a vector memory remove request",
                            CapabilitySchema.For(typeof(VectorMemoryRemoved)),
                            Array.Empty<string>()),
                        new SynapseCapabilityDescriptor(
                            "memory.vector-stored",
                            1,
                            "Result of a vector memory store request",
                            CapabilitySchema.For(typeof(VectorMemoryStored)),
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
            builder, typeof(MemoryModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
