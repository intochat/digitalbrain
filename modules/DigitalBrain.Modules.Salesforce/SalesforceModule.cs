using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Integrations.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(SalesforceRuntimeOptions.Default);
    }
}
