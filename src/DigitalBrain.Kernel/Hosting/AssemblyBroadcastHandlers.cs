using System.Reflection;

namespace DigitalBrain.Kernel;

internal sealed class AssemblyBroadcastHandlers(Assembly assembly) : IConfigureBroadcastCatalog
{
    public void Configure(BroadcastCatalog catalog) => catalog.AddAssembly(assembly);
}
