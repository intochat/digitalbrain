using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
    }
}
