using DigitalBrain.AI;
using DigitalBrain.Assistant;
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

namespace DigitalBrain.Kernel
{
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
}

namespace DigitalBrain.Core
{
    internal static class DigitalBrainSiloHostExtensions
    {
        internal static ISiloBuilder AddDigitalBrain(this ISiloBuilder builder)
        {
            DigitalBrainRuntime.Add(builder, DigitalBrain.Kernel.CompiledModuleCatalog.Modules);
            return builder;
        }
    }
}
