using DigitalBrain.AI;
using DigitalBrain.Assistant;
using DigitalBrain.Aspire;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Introspection;
using DigitalBrain.Memory;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Modules.Sdk.Webhook;
using DigitalBrain.Salesforce;
using DigitalBrain.Shell;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Kernel;

internal static class CompiledModuleCatalog
{
    internal static IReadOnlyList<ICompiledModule> Modules { get; } =
    [
        new AIModule(),
        new AssistantModule(),
        new ChatModule(),
        new GoogleModule(),
        new IntrospectionModule(),
        new MemoryModule(),
        new McpModule(),
        new WebhookModule(),
        new SalesforceModule(),
        new ShellModule(),
        new TasksModule(),
        new TimeModule(),
    ];
}

internal static class DigitalBrainHost
{
    internal static IHostApplicationBuilder AddDigitalBrain(this IHostApplicationBuilder builder)
        => builder.AddDigitalBrain(CompiledModuleCatalog.Modules);
}
