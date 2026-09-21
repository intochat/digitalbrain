using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.ClickHouse;
using DigitalBrain.Coding;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Memory;
using DigitalBrain.Microsoft;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;

namespace IntoChat.Tests.E2E;

/// <summary>Overrides for the real application graph. Scenarios own data and protocol fixtures.</summary>
internal static class IntoChatE2ETest
{
    // Port 1 is deliberately unserved: unused provider calls fail instead of reaching live services.
    private static readonly Uri UnconfiguredProvider = new("http://127.0.0.1:1/");

    public static Task<E2EBrain> StartAsync(CancellationToken ct) => Create().StartAsync(ct);

    public static E2ETestBuilder<Projects.IntoChat_AppHost> Create(string modelApiKey = "fixture-key", Dictionary<string, string?>? privateConfiguration = null)
        => E2ETest.For<Projects.IntoChat_AppHost>()
            .ConfigureModule<AIModule>(ai => ai.WithoutLocalModels().WithoutVoiceToText().WithoutWebSearch()
                .WithDefaultLlm<IGpt56Luna>().WithModelEndpoint(AiProvider.OpenAI, new(UnconfiguredProvider, "v1/")))
            .ConfigureModule<MemoryModule>(memory => memory.WithQdrant())
            .ConfigureModule<ClickHouseModule>(database => database.WithClickHouse(options =>
            {
                options.PersistentStorage = false;
                options.WithSeed("leads");
            }))
            .ConfigureModule<SupabaseModule>(database => database.WithPostgres())
            .ConfigureModule<GoogleModule>(google => google.WithTokenEndpoint(new(UnconfiguredProvider, "token")))
            .ConfigureModule<SalesforceModule>(salesforce => salesforce.WithLocalMcp(new(UnconfiguredProvider, "mcp")))
            .ConfigureModule<MicrosoftModule>(microsoft => microsoft.WithoutAspire()
                .WithGitHubRepositories(new Dictionary<string, GitHubRepositoryDeclaration>()))
            .ConfigureModule<CodingModule>(coding => coding.ConfigureOptions<CodingModuleOptions>(
                options => options.SolutionPath = null, "SolutionPath"))
            .ConfigureModule<FlutterModule>(flutter => flutter.BackendOnly())
            .WithExecution(new()
            {
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
