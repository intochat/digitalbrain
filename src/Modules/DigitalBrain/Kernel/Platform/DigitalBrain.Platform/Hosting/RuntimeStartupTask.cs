using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Signals;
using DigitalBrain.Kernel;

namespace DigitalBrain.Platform.Hosting;

internal sealed class RuntimeStartupTask(ModuleInventory modules, LocalSignalHub signals) : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken)
    {
        foreach (var module in modules.Types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signals.Publish(new ModuleLoaded(module.AssemblyQualifiedName!));
        }
        return Task.CompletedTask;
    }
}
