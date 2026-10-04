using DigitalBrain.Aspire.Server;

namespace DigitalBrain.OS;

// The one place the operating system's module composition is named. Every host of the OS —
// the product distribution, the module test host's reference composition, the OS E2E fixture —
// registers modules through this catalog instead of keeping its own list. A distribution is a
// diff against this composition, not a copy of it.
public static class OsCatalog
{
    public static IReadOnlyList<(string Id, Type Module)> Modules { get; } =
    [
        ("ai", typeof(DigitalBrain.AI.AIModule)),
        ("clickhouse", typeof(DigitalBrain.ClickHouse.ClickHouseModule)),
        ("apps", typeof(DigitalBrain.Apps.AppsModule)),
        ("assistant", typeof(DigitalBrain.Assistant.AssistantModule)),
        ("compute", typeof(DigitalBrain.Compute.ComputeModule)),
        ("registry", typeof(DigitalBrain.Registry.RegistryModule)),
        ("specs", typeof(DigitalBrain.Specs.SpecsModule)),
        ("files", typeof(DigitalBrain.Files.FilesModule)),
        ("flutter", typeof(DigitalBrain.Flutter.FlutterModule)),
        ("gmail", typeof(DigitalBrain.Google.Gmail.GmailModule)),
        ("aspire", typeof(DigitalBrain.Microsoft.Aspire.AspireModule)),
        ("csharp-authoring", typeof(DigitalBrain.Microsoft.CSharp.CSharpAuthoringModule)),
        ("csharp", typeof(DigitalBrain.Microsoft.CSharp.CSharpModule)),
        ("github", typeof(DigitalBrain.Microsoft.GitHub.GitHubModule)),
        ("playwright", typeof(DigitalBrain.Microsoft.Playwright.PlaywrightModule)),
        ("postgres", typeof(DigitalBrain.Postgres.PostgresModule)),
        ("qdrant", typeof(DigitalBrain.Qdrant.QdrantModule)),
        ("salesforce", typeof(DigitalBrain.Salesforce.SalesforceModule)),
        ("supabase", typeof(DigitalBrain.Supabase.SupabaseModule)),
        ("time", typeof(DigitalBrain.Time.TimeModule)),
    ];

    public static DigitalBrainServerBuilder AddOperatingSystem(this DigitalBrainServerBuilder server)
    {
        ArgumentNullException.ThrowIfNull(server);
        foreach (var (id, module) in Modules)
        {
            server.AddModule(id, () => (Kernel.IModule)Activator.CreateInstance(module)!);
        }
        return server;
    }
}
