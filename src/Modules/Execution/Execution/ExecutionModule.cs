using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Execution;

public sealed class ExecutionModule : IModule
{
    public const string HarnessWorkerGrainType = "worker";
    public const string ChatTurnWorkerGrainType = "chat-turn-worker";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Seed known worker grain types; other modules may AddSingleton<IWorkerTypeRegistration>.
        builder.Services.AddSingleton<IWorkerTypeRegistration>(
            new WorkerTypeRegistration(HarnessWorkerGrainType));
        builder.Services.AddSingleton<IWorkerTypeRegistration>(
            new WorkerTypeRegistration(ChatTurnWorkerGrainType));

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
