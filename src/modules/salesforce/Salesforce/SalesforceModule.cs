using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;

namespace DigitalBrain.Salesforce;

public sealed partial class SalesforceModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
    }
}
