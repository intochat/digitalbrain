using DigitalBrain.Modules.Sdk.Mcp;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
    }
}
