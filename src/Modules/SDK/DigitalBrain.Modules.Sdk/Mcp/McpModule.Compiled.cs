using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Sdk.Mcp;

public sealed partial class McpModule : ICompiledModule
{
    public static ModuleId Id { get; } =
        new("DigitalBrain.Modules.Sdk.Mcp.McpModule");

    ModuleId ICompiledModule.Id => Id;

    public static CapabilityManifest Capabilities { get; } =
        new(
            Id,
            "1.0.0",
            "McpModule module",
            [
                new NeuronCapabilityDescriptor(
                    "mcp.authorization",
                    "MCP OAuth authorization neuron",
                    "default",
                    [],
                    [
                        new SynapseCapabilityDescriptor(
                            "db.mcp.authorization-completed",
                            1,
                            "MCP authorization completed",
                            CapabilitySchema.For(typeof(AuthorizationCompleted))),
                        new SynapseCapabilityDescriptor(
                            "db.mcp.authorization-denied",
                            1,
                            "MCP authorization was denied",
                            CapabilitySchema.For(typeof(AuthorizationDenied))),
                        new SynapseCapabilityDescriptor(
                            "db.mcp.authorization-required",
                            1,
                            "MCP server requires interactive authorization",
                            CapabilitySchema.For(typeof(AuthorizationRequired))),
                    ]),
            ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
        => ConfigureSerialization(services);

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder, typeof(McpModule).Assembly);
    }

    static partial void ConfigureSerialization(IServiceCollection services);

    static partial void ConfigureRuntime(ISiloBuilder builder);
}
