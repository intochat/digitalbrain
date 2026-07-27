using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainSiloBuilderExtensions
{
    public static ISiloBuilder AddBroadcastHandlers(this ISiloBuilder builder, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assembly);

        builder.Services.AddSingleton<IConfigureBroadcastCatalog>(new AssemblyBroadcastHandlers(assembly));

        return builder;
    }
}
