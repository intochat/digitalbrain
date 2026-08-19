using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// Silo contracts + implementation assemblies. AppHost AddModule<> is the
// product composition root (see AppHost.cs) — keep these lists aligned when
// shipping a new module into the silo.
public static class ProductModules
{
    public static ModuleAssemblies Assemblies { get; } = new(
        [
            typeof(DigitalBrain.Abstractions.DigitalBrainNames).Assembly,
            typeof(DigitalBrain.AI.IAssistant).Assembly,
            typeof(DigitalBrain.Memory.IVectorMemory).Assembly,
            typeof(DigitalBrain.Time.StartTimer).Assembly,
            typeof(DigitalBrain.Chat.SendMessage).Assembly,
        ],
        [
            typeof(DigitalBrain.AI.AIModule).Assembly,
            typeof(DigitalBrain.Memory.MemoryModule).Assembly,
            typeof(DigitalBrain.Time.TimerNeuron).Assembly,
            typeof(DigitalBrain.UI.UiModule).Assembly,
        ]);
}
