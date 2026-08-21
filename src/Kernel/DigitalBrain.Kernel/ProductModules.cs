using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// The Kernel is the only runtime module catalog. AppHost only projects external
// resources and configuration into this process; it does not choose silo modules.
public static class ProductModules
{
    public static ModuleAssemblies Assemblies { get; } = new(
        [
            typeof(DigitalBrain.AI.AIModule).Assembly,
            typeof(DigitalBrain.Memory.MemoryModule).Assembly,
            typeof(DigitalBrain.Time.TimerNeuron).Assembly,
            typeof(DigitalBrain.UI.UiModule).Assembly,
        ]);
}
