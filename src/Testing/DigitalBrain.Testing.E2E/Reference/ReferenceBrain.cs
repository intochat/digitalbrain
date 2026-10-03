using DigitalBrain.Postgres;
using DigitalBrain.Microsoft.Playwright;
using DigitalBrain.AI;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Specs;
using DigitalBrain.ClickHouse;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Memory;
using DigitalBrain.Qdrant;
using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Compute;
using DigitalBrain.Registry;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Platform.Contracts.Secrets;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;
using DigitalBrain.Time;
using DigitalBrain.Files;

namespace DigitalBrain.Testing.E2E;

public static class ReferenceBrain
{
    private static readonly Uri UnconfiguredProvider = new("http://127.0.0.1:1/");

    public static E2ETestBuilder Create(string modelApiKey = "fixture-key", Dictionary<string, string?>? privateConfiguration = null)
        => E2ETest.Create()
            .WithModule<AIModule, AIOptions>(ai =>
            {
                ai.Telemetry.EnableSensitiveData = true;

                ai.WithLlm<IGpt56Luna>()
                    .WithDefaultLlm<IGemma4>()
                    .WithDefaultEmbedding<ITextEmbedding3Small>()
                    .WithVoiceToText<IWhisperLargeV3Turbo>()
                    .WithTavilySearch();
            })
            .WithModule<QdrantModule, QdrantModuleOptions>(qdrant => qdrant.WithHostedQdrant())
            .WithModule<MemoryModule>()
            .WithModule<ClickHouseModule, ClickHouseModuleOptions>(database => database.WithClickHouse())
            .WithModule<SupabaseModule, SupabaseModuleOptions>(database => database.WithConnection("supabase"))
            .WithModule<PostgresModule, PostgresModuleOptions>(database => database.WithPostgres(options => options.DatabaseName = "digitalbrain"))
            .WithModule<PlaywrightModule>()
            .WithModule<TimeModule>()
            .WithModule<FilesModule>()
            .WithModule<GmailModule, GmailModuleOptions>(gmail => gmail.WithGmail())
            .WithModule<SalesforceModule, SalesforceModuleOptions>(salesforce => salesforce.WithHostedMcp())
            .WithModule<GitHubModule>()
            .WithModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.RunDesktopApp())
            .WithModule<ComputeModule>()
            .WithModule<RegistryModule>()
            .WithModule<SpecsModule>()
            .WithModule<AppsModule>()
            .WithModule<AssistantModule>()
            .WithModule<AspireModule>()
            .WithModule<CSharpModule>()
            .WithModule<CSharpAuthoringModule>()
            .ConfigureModule<AIModule, AIOptions>(ai => ai.WithoutLocalModels().WithoutVoiceToText().WithoutWebSearch()
                .WithDefaultLlm<IGpt56Luna>().WithModelEndpoint(AiProvider.OpenAI, new(UnconfiguredProvider, "v1/")))
            .ConfigureModule<QdrantModule, QdrantModuleOptions>(qdrant => qdrant.Host = false)
            .ConfigureModule<PostgresModule, PostgresModuleOptions>(database => database.WithPostgres(options =>
            {
                options.DatabaseName = "digitalbrain";
                options.PersistentStorage = false;
            }))
            .ConfigureModule<ClickHouseModule, ClickHouseModuleOptions>(database => database.WithClickHouse(options =>
            {
                options.PersistentStorage = false;
            }))
            .ConfigureModule<SupabaseModule, SupabaseModuleOptions>(database => database.WithPostgres())
            .ConfigureModule<GmailModule, GmailModuleOptions>(gmail => gmail.WithTokenEndpoint(new(UnconfiguredProvider, "token")))
            .ConfigureModule<SalesforceModule, SalesforceModuleOptions>(salesforce => salesforce.WithLocalMcp(new(UnconfiguredProvider, "mcp")))
            .ConfigureModule<GitHubModule, GitHubModuleOptions>(github => github.WithGitHubRepositories(new Dictionary<string, GitHubRepositoryDeclaration>()))
            .ConfigureModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.BackendOnly())
            .WithExecution(new()
            {
                // Use the authenticated reference host without product-owned shipped content.
                ResourceEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DigitalBrain__Testing__ReferenceComposition"] = "true",
                },
                PrivateConfiguration = Merge(new Dictionary<string, string?>
                {
                    ["Parameters:openai-api-key"] = modelApiKey,
                    ["Parameters:gmail-client-id"] = "fixture-client",
                    ["Parameters:gmail-client-secret"] = "fixture-secret",
                    ["Parameters:salesforce-consumer-key"] = "fixture-client",
                    ["Parameters:salesforce-consumer-secret"] = "fixture-secret",
                }, privateConfiguration),
            });
    private static Dictionary<string, string?> Merge(Dictionary<string, string?> defaults, Dictionary<string, string?>? extra)
    {
        if (extra is not null) { foreach (var item in extra) { defaults[item.Key] = item.Value; } }
        return defaults;
    }
}
