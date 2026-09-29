using DigitalBrain.Contracts;

namespace DigitalBrain.Core;

internal sealed class RuntimeStartupTask(ModuleInventory modules, RuntimeSignals signals) : IStartupTask
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