using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

public sealed partial class AIModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.AI.AIModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "AIModule module",
            Array.Empty<string>(),
            Array.Empty<NeuronCapabilityDescriptor>());

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(AIModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
