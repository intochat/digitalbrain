using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using DigitalBrain.Behaviors.Host;

namespace DigitalBrain.Behaviors.Tests;

public sealed class InProcessBehaviorHostGatewayModule : IModule, ICompiledModule
{
    public static ModuleId Id { get; } = new("digitalbrain.behaviors.host.inprocess");

    ModuleId ICompiledModule.Id => Id;

    CapabilityManifest ICompiledModule.Capabilities { get; } = new(
        new ModuleId("digitalbrain.behaviors.host.inprocess"),
        "1.0.0",
        "In-process behavior host gateway",
        [],
        []);

    public void PrepareSerialization(IServiceCollection services)
    {
    }

    public void Activate(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<BehaviorHostEngine>();
        builder.Services.AddSingleton<IBehaviorHostGateway>(static provider =>
            provider.GetRequiredService<BehaviorHostEngine>());
    }
}
