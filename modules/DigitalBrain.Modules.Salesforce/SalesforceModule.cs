using DigitalBrain.Abstractions;
using DigitalBrain.Integrations.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Salesforce;

public sealed partial class SalesforceModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(SalesforceRuntimeOptions.Default);
    }
}
