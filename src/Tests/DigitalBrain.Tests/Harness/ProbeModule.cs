using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Harness;

public sealed class ProbeModule : IModule, ICompiledModule
{
    public static ModuleId Id { get; } = new("DigitalBrain.Tests.Probe");

    ModuleId ICompiledModule.Id => Id;

    void ICompiledModule.PrepareSerialization(IServiceCollection services)
    {
    }

    void ICompiledModule.Activate(ISiloBuilder builder)
    {
        builder.AddBroadcastHandlers(typeof(ProbeModule).Assembly);
        builder.Services.AddSingleton<ISynapseTransform>(new ProbeFactToItemAppended());
        builder.Services.AddSingleton<ISynapseTransform>(new ProbeFactToChartPoint());
        builder.Services.AddSingleton<ISynapseTransform>(new PoisonTransform());
    }
}
