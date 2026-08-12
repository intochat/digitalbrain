using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// Silo contracts + implementation assemblies. AppHost AddModule<> is the
// product composition root (see AppHost.cs) — keep these lists aligned when
// shipping a new module into the silo.
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
            typeof(DigitalBrain.Conversations.IConversation).Assembly,
            typeof(DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron).Assembly,
        ],
        [
            typeof(DigitalBrain.AI.AIModule).Assembly,
            typeof(DigitalBrain.Google.GoogleModule).Assembly,
            typeof(DigitalBrain.Introspection.IntrospectionNeuron).Assembly,
            typeof(DigitalBrain.Memory.MemoryModule).Assembly,
            typeof(DigitalBrain.Salesforce.SalesforceModule).Assembly,
            typeof(DigitalBrain.Execution.ExecutionNeuron).Assembly,
            typeof(DigitalBrain.Behavior.BehaviorNeuron).Assembly,
            typeof(DigitalBrain.Kinds.KindsModule).Assembly,
            typeof(DigitalBrain.Library.LibraryNeuron).Assembly,
            typeof(DigitalBrain.Corpus.CorpusNeuron).Assembly,
            typeof(DigitalBrain.Repository.RepositoryNeuron).Assembly,
            typeof(DigitalBrain.Time.TimerNeuron).Assembly,
            typeof(DigitalBrain.UI.UiModule).Assembly,
            typeof(DigitalBrain.Os.WorkspaceNeuron).Assembly,
            typeof(DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron).Assembly,
        ]);
}
