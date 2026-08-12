using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// Single silo product catalog. AppHost Aspire projections live in
// AppHost/ProductComposition.cs — keep AspireProjectedModuleNames in sync there.
//
// Adding a product surface:
//   1) Contracts + implementation assemblies here
//   2) ProductComposition.AddModule when the surface needs AppHost resources
//      (LLM, Qdrant, Flutter, OAuth). Kernel-only surfaces (Time, Execution,
//      Introspection, Sdk) stay silo-only.
public static class ProductModules
{
    public static ModuleAssemblies Assemblies { get; } = new(
        [
            typeof(DigitalBrain.Abstractions.Graph.ISynapseGraph).Assembly,
            typeof(DigitalBrain.AI.IAssistant).Assembly,
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

    // Canonical names of modules that ProductComposition must AddModule.
    public static IReadOnlyList<string> AspireProjectedModuleNames { get; } =
    [
        "DigitalBrain.AI.AIModule",
        "DigitalBrain.Memory.MemoryModule",
        "DigitalBrain.UI.UiModule",
        "DigitalBrain.Google.GoogleModule",
        "DigitalBrain.Salesforce.SalesforceModule",
    ];
}
