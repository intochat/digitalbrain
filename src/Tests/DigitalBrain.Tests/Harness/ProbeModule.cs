using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Harness;

public sealed class ProbeModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ISynapseTransform>(new ProbeFactToItemAppended());
        builder.Services.AddSingleton<ISynapseTransform>(new ProbeFactToChartPoint());
        builder.Services.AddSingleton<ISynapseTransform>(new PoisonTransform());
    }
}
