using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Kernel;
namespace DigitalBrain.Testing.E2E;
internal static class ModuleHostingAdapters
{
    internal static IDigitalBrainModuleHosting? For(Type module) => module switch
    {
        var type when type == typeof(DigitalBrain.AI.AIModule) => new DigitalBrain.AI.Aspire.Hosting.AIModuleHosting(),
        var type when type == typeof(DigitalBrain.ClickHouse.ClickHouseModule) => new DigitalBrain.ClickHouse.Aspire.Hosting.ClickHouseModuleHosting(),
        var type when type == typeof(DigitalBrain.Flutter.FlutterModule) => new DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting(),
        var type when type == typeof(DigitalBrain.Google.Gmail.GmailModule) => new DigitalBrain.Google.Gmail.GmailModuleHosting(),
        var type when type == typeof(DigitalBrain.Microsoft.Aspire.AspireModule) => new DigitalBrain.Microsoft.Aspire.AspireModuleHosting(),
        var type when type == typeof(DigitalBrain.Microsoft.CSharp.CSharpAuthoringModule) => new DigitalBrain.Microsoft.CSharp.CSharpAuthoringModuleHosting(),
        var type when type == typeof(DigitalBrain.Microsoft.CSharp.CSharpModule) => new DigitalBrain.Microsoft.CSharp.CSharpModuleHosting(),
        var type when type == typeof(DigitalBrain.Microsoft.GitHub.GitHubModule) => new DigitalBrain.Microsoft.GitHub.GitHubModuleHosting(),
        var type when type == typeof(DigitalBrain.Postgres.PostgresModule) => new DigitalBrain.Postgres.Aspire.Hosting.PostgresModuleHosting(),
        var type when type == typeof(DigitalBrain.Qdrant.QdrantModule) => new DigitalBrain.Qdrant.Aspire.Hosting.QdrantModuleHosting(),
        var type when type == typeof(DigitalBrain.Salesforce.SalesforceModule) => new DigitalBrain.Salesforce.Aspire.Hosting.SalesforceModuleHosting(),
        var type when type == typeof(DigitalBrain.Supabase.SupabaseModule) => new DigitalBrain.Supabase.Aspire.Hosting.SupabaseModuleHosting(),
        _ => null,
    };
    internal static DigitalBrainBuilder AddModules(this DigitalBrainBuilder brain, IReadOnlyList<ModuleDefinition> modules)
    {
        foreach (var module in ModuleComposition.Resolve(modules))
        { brain.WithModule(ModuleIdentity.Get(module.ModuleType), For(module.ModuleType), module.Configuration); }
        return brain;
    }
}
