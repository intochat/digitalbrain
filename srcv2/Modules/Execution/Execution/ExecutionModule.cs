using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Execution;

public sealed class ExecutionModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(static services =>
        {
            var registry = new WorkerGrainTypeRegistry();
            foreach (var registration in services.GetServices<IWorkerTypeRegistration>())
            {
                if (!string.IsNullOrWhiteSpace(registration.GrainType))
                {
                    registry.Allow(registration.GrainType);
                }
            }

            return registry;
        });
    }
}
