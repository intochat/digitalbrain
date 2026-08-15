using System.Reflection;

namespace DigitalBrain.Core;

internal sealed class AssemblyBroadcastHandlers(Assembly assembly) : IConfigureBroadcastCatalog
{
    public void Configure(BroadcastCatalog catalog) => catalog.AddAssembly(assembly);
}
