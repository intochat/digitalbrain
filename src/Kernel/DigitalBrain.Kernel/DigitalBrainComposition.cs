using DigitalBrain.Aspire;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Kernel;

internal static class ComposedModules
{
    internal static ModuleAssemblies Assemblies { get; } = new(
        [
            typeof(DigitalBrain.Abstractions.ISynapseGraph).Assembly,
            typeof(DigitalBrain.AI.IAssistant).Assembly,
            typeof(DigitalBrain.Google.IGmail).Assembly,
            typeof(DigitalBrain.Introspection.ReadTopologyRequest).Assembly,
            typeof(DigitalBrain.Memory.IVectorMemory).Assembly,

            typeof(DigitalBrain.Execution.IExecution).Assembly,
            typeof(DigitalBrain.Time.StartTimer).Assembly,
            typeof(DigitalBrain.Chat.SendMessage).Assembly,
            typeof(DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron).Assembly,
        ],
        [
            typeof(DigitalBrain.AI.AIModule).Assembly,
            typeof(DigitalBrain.Google.GoogleModule).Assembly,
            typeof(DigitalBrain.Introspection.IntrospectionNeuron).Assembly,
            typeof(DigitalBrain.Memory.MemoryModule).Assembly,
            typeof(DigitalBrain.Salesforce.SalesforceModule).Assembly,
            typeof(DigitalBrain.Execution.ExecutionNeuron).Assembly,
            typeof(DigitalBrain.Time.TimerNeuron).Assembly,
            typeof(DigitalBrain.UI.UiModule).Assembly,
            typeof(DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron).Assembly,
        ]);
}

internal static class DigitalBrainHost
{
    internal static IHostApplicationBuilder AddDigitalBrain(this IHostApplicationBuilder builder)
        => builder.AddDigitalBrain(ComposedModules.Assemblies);
}
