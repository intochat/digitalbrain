using DigitalBrain.Abstractions;
using DigitalBrain.Integrations.Mcp;
using Orleans.Hosting;

namespace DigitalBrain.Google;

public sealed partial class GoogleModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
    }
}
