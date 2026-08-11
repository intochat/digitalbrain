using DigitalBrain.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.UI;

public sealed class UiModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Pin the adapter grain type on the worker allow-list registration surface.
        // ExecutionModule seeds the registry with the same name at composition.
        builder.Services.AddSingleton<IWorkerTypeRegistration>(
            new WorkerTypeRegistration(ChatTurnWorker.GrainTypeName));
    }
}
